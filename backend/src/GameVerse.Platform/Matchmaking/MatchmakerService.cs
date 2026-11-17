using System.Security.Cryptography;
using GameVerse.Contracts.Catalog;
using GameVerse.Contracts.Session;
using GameVerse.Platform.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameVerse.Platform.Matchmaking;

/// <summary>
/// Forms games from the queues (CLAUDE.md 4.6, ADR-0010). Runs on every API instance once a second; a Redis lock lets one
/// instance work a queue per tick, and an atomic claim means a player can never land in two games.
/// <list type="bullet">
/// <item>Ranked: the oldest player anchors a game and takes the closest players that both sides' rating bands accept.
/// Bands widen with the wait. Never AI.</item>
/// <item>Casual: first in, first out, ratings ignored. After a wait the oldest player's game fills up with AI seats.</item>
/// </list>
/// Wait times use the injected <see cref="TimeProvider"/>; the tick itself is wall clock.
/// </summary>
internal sealed partial class MatchmakerService(
    QueueStore store, IServiceScopeFactory scopes, MatchmakingOptions options, TimeProvider time, ILogger<MatchmakerService> log) : BackgroundService
{
    private static readonly TimeSpan LockHold = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.TickMilliseconds));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // A bad tick must not stop matchmaking; the next tick tries again.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                TickFailed(log, ex);
            }
        }
    }

    /// <summary>One pass over every queue. Returns the number of games formed.</summary>
    internal async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var formed = 0;
        foreach (var queue in await store.QueuesAsync().ConfigureAwait(false))
        {
            var token = await store.TryLockAsync(queue, LockHold).ConfigureAwait(false);
            if (token is null)
            {
                continue; // another instance is on it
            }

            try
            {
                formed += await MatchQueueAsync(queue, ct).ConfigureAwait(false);
                await store.ForgetIfEmptyAsync(queue).ConfigureAwait(false);
            }
            finally
            {
                await store.UnlockAsync(queue, token).ConfigureAwait(false);
            }
        }

        return formed;
    }

    private async Task<int> MatchQueueAsync(string queue, CancellationToken ct)
    {
        var waiting = (await store.WaitingAsync(queue).ConfigureAwait(false)).ToList();
        var now = time.GetUtcNow();
        var formed = 0;
        while (waiting.Count > 0)
        {
            var anchor = waiting[0];
            var group = anchor.ModeKind == ModeKinds.OnlineRanked ? RankedGroup(anchor, waiting, now) : CasualGroup(anchor, waiting, now);
            if (group is null)
            {
                if (anchor.ModeKind != ModeKinds.OnlineRanked)
                {
                    break; // casual is first in, first out: if the oldest cannot start, nobody can
                }

                waiting.RemoveAt(0); // ranked: this anchor waits for a wider band; try the next one
                continue;
            }

            waiting.RemoveAll(group.Contains);
            if (await StartAsync(queue, anchor, group, now, ct).ConfigureAwait(false))
            {
                formed++;
            }
        }

        return formed;
    }

    /// <summary>The anchor plus the closest players both sides' bands accept, or null when there are not enough yet.</summary>
    private List<QueueEntry>? RankedGroup(QueueEntry anchor, List<QueueEntry> waiting, DateTimeOffset now)
    {
        var anchorBand = options.Band(Waited(anchor, now));
        var picked = waiting.Skip(1)
            .Where(other => Accepts(anchorBand, anchor, other) && Accepts(options.Band(Waited(other, now)), other, anchor))
            .OrderBy(other => Math.Abs(other.Rating - anchor.Rating))
            .ThenBy(other => other.JoinedAt)
            .Take(anchor.PlayerCount - 1)
            .ToList();
        return picked.Count == anchor.PlayerCount - 1 ? [anchor, .. picked] : null;
    }

    /// <summary>The oldest players in order; short of a full game, the anchor's game starts with AI once it has waited enough.</summary>
    private List<QueueEntry>? CasualGroup(QueueEntry anchor, List<QueueEntry> waiting, DateTimeOffset now)
    {
        var humans = waiting.Take(anchor.PlayerCount).ToList();
        return humans.Count == anchor.PlayerCount || Waited(anchor, now).TotalSeconds >= options.AiFillSeconds ? humans : null;
    }

    private async Task<bool> StartAsync(string queue, QueueEntry anchor, List<QueueEntry> humans, DateTimeOffset now, CancellationToken ct)
    {
        if (!await store.ClaimAsync(queue, humans.Select(h => h.UserId).ToList()).ConfigureAwait(false))
        {
            return false; // someone cancelled meanwhile; they will be looked at again next tick
        }

        // Seats in random order, so the longest waiter is not always seat 0; empty seats (casual only) get AI.
        var seats = humans.Select(h => new NewSeat(h.UserId, false, null))
            .Concat(Enumerable.Range(0, anchor.PlayerCount - humans.Count).Select(_ => new NewSeat(null, true, options.AiLevel)))
            .ToArray();
        RandomNumberGenerator.Shuffle(seats.AsSpan());

        Guid? sessionId;
        CreateSessionError error;
        await using (var scope = scopes.CreateAsyncScope())
        {
            (sessionId, error) = await scope.ServiceProvider.GetRequiredService<ISessionService>().CreateAsync(
                anchor.GameId, anchor.ModeKey, humans[0].UserId, seats, new SessionConfig<Dictionary<string, string>> { HouseRules = [] }, ct).ConfigureAwait(false);
        }

        if (error != CreateSessionError.None)
        {
            SessionFailed(log, queue, error.ToString());
            await store.RestoreAsync(humans).ConfigureAwait(false);
            return false;
        }

        foreach (var human in humans)
        {
            await store.MarkMatchedAsync(human.UserId, new MatchedTicket
            {
                SessionId = sessionId!.Value,
                GameId = anchor.GameId,
                ModeKey = anchor.ModeKey,
                PlayerCount = anchor.PlayerCount,
                WaitedSeconds = (int)Waited(human, now).TotalSeconds,
            }, TimeSpan.FromMinutes(options.MatchedMinutes)).ConfigureAwait(false);
        }

        Matched(log, queue, sessionId!.Value, humans.Count, anchor.PlayerCount - humans.Count);
        return true;
    }

    private static bool Accepts(int? band, QueueEntry self, QueueEntry other) => band is null || Math.Abs(self.Rating - other.Rating) <= band;

    private static TimeSpan Waited(QueueEntry entry, DateTimeOffset now) => now - DateTimeOffset.FromUnixTimeMilliseconds(entry.JoinedAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "GV_MATCHMAKING {Queue}: session {SessionId} with {Humans} players and {Ai} AI")]
    private static partial void Matched(ILogger logger, string queue, Guid sessionId, int humans, int ai);

    [LoggerMessage(Level = LogLevel.Error, Message = "GV_MATCHMAKING {Queue}: could not create the session ({Error}); players returned to the queue")]
    private static partial void SessionFailed(ILogger logger, string queue, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "GV_MATCHMAKING tick failed")]
    private static partial void TickFailed(ILogger logger, Exception exception);
}
