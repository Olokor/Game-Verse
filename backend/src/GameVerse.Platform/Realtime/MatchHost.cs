using System.Text.Json;
using System.Threading.Channels;
using GameVerse.Contracts.Catalog;
using GameVerse.Contracts.Events;
using GameVerse.Contracts.Realtime;
using GameVerse.Contracts.Session;
using GameVerse.Platform.Sessions;
using GameVerse.Rules.Random;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace GameVerse.Platform.Realtime;

/// <summary>
/// The one authority for one live match (CLAUDE.md 4.5, ADR-0009). An actor: a single loop reads its
/// <see cref="Channel{T}"/> mailbox and owns the game state, so actions apply strictly in order with no locks.
/// <para>
/// Messages reach it from connections on any instance through a Redis list (the inbox), which a pump drains into the
/// mailbox. A list, not plain pub/sub, so intents sent while the host is moving between instances wait instead of
/// vanishing. Output goes back through a pub/sub channel every instance's connections listen on.
/// </para>
/// <para>
/// Every accepted action is appended to the Postgres move log (the source of truth), then snapshotted to Redis, then
/// broadcast as each seat's own view. A new host resumes from the snapshot plus any newer logged moves.
/// </para>
/// </summary>
internal sealed class MatchHost(
    Guid sessionId,
    string instanceId,
    MatchHostRegistry registry,
    IServiceScopeFactory scopes,
    IConnectionMultiplexer redis,
    IMatchGames games,
    RealtimeOptions options,
    TimeProvider time,
    ILogger<MatchHost> log) : IDisposable
{
    /// <summary>How long a finished match stays loaded to answer late reconnects with the result.</summary>
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan SnapshotAfterEnd = TimeSpan.FromHours(1);

    /// <summary>Separates the AI's PRNG from the dice PRNG, which use the same session seed.</summary>
    private const ulong AiSeedDomain = 0x9E3779B97F4A7C15UL;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Channel<HostInbound> mailbox = Channel.CreateUnbounded<HostInbound>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Channel<bool> pokes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly CancellationTokenSource stopping = new();

    private LiveSession live = null!;
    private IMatchGame game = null!;
    private object state = null!;
    private long version;
    private Xoshiro256StarStar aiRng = null!;
    private Dictionary<int, long> lastClientSeq = [];
    private Dictionary<string, SnapshotConnection> connections = [];
    private Dictionary<int, long> graceEnds = [];
    private long? turnDeadline;
    private DateTimeOffset? aiDue;
    private DateTimeOffset? lingerUntil;
    private DateTimeOffset idleSince;
    private MatchEndedEvent? ended;
    private bool releaseLease = true;
    private bool disposed;

    public Guid SessionId => sessionId;

    private bool Online => live.ModeKind is ModeKinds.OnlinePrivate or ModeKinds.OnlineRanked or ModeKinds.OnlineCasual;

    private TimeSpan Grace => TimeSpan.FromSeconds(live.ModeKind == ModeKinds.OnlineRanked ? options.GraceRankedSeconds : options.GraceCasualSeconds);

    private long Now => time.GetUtcNow().ToUnixTimeMilliseconds();

    /// <summary>Stops the host and releases its lease (graceful shutdown).</summary>
    public void Stop() => Cancel();

    /// <summary>Stops the host as a crash would: the lease is left to expire. Tests use this to prove resume.</summary>
    public void Abort()
    {
        releaseLease = false;
        Cancel();
    }

    public void Dispose()
    {
        lock (stopping)
        {
            disposed = true;
            stopping.Dispose();
        }
    }

    private void Cancel()
    {
        lock (stopping)
        {
            if (!disposed)
            {
                stopping.Cancel();
            }
        }
    }

    public async Task RunAsync(CancellationToken shutdown)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(shutdown, stopping.Token);
        var ct = cts.Token;
        var subscriber = redis.GetSubscriber();
        ChannelMessageQueue? pokeQueue = null;
        Task? pump = null;
        Task? lease = null;
        try
        {
            if (!await LoadAsync(ct).ConfigureAwait(false))
            {
                return;
            }

            pokeQueue = await subscriber.SubscribeAsync(RealtimeKeys.InboxPoke(sessionId)).ConfigureAwait(false);
            pokeQueue.OnMessage(_ => pokes.Writer.TryWrite(true));
            pump = PumpInboxAsync(ct);
            lease = RenewLeaseAsync(cts, ct);
            RealtimeLog.HostStarted(log, sessionId, instanceId, version);
            await LoopAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Stopped, aborted or shutting down.
        }
#pragma warning disable CA1031 // A failing match must not take the process down; the next connection or the watchdog resumes it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            RealtimeLog.HostFailed(log, ex, sessionId);
        }
        finally
        {
            await cts.CancelAsync().ConfigureAwait(false);
            if (pokeQueue is not null)
            {
                await pokeQueue.UnsubscribeAsync().ConfigureAwait(false);
            }

            await Quietly(pump).ConfigureAwait(false);
            await Quietly(lease).ConfigureAwait(false);
            if (releaseLease)
            {
                await registry.ReleaseLeaseAsync(sessionId).ConfigureAwait(false);
            }

            registry.Remove(this);
            Dispose();
            RealtimeLog.HostStopped(log, sessionId, releaseLease);
        }
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var found = await sessions.GetLiveAsync(sessionId, ct).ConfigureAwait(false);
        if (found is null || games.Find(found.GameId) is not { } matchGame || found.ModeKind is ModeKinds.Local or ModeKinds.Solo)
        {
            return false;
        }

        live = found;
        game = matchGame;
        if (live.Status == SessionStatuses.Created)
        {
            await sessions.MarkActiveAsync(sessionId, ct).ConfigureAwait(false);
        }

        var stored = await redis.GetDatabase().StringGetAsync(RealtimeKeys.Snapshot(sessionId)).ConfigureAwait(false);
        var snapshot = stored.IsNullOrEmpty ? null : JsonSerializer.Deserialize<MatchSnapshot>(stored.ToString(), Json);
        var seed = live.Seed ?? 0;
        if (snapshot is null)
        {
            state = game.NewGame(live.HouseRules, live.Seats.Count, seed);
            aiRng = new Xoshiro256StarStar(seed ^ AiSeedDomain);
        }
        else
        {
            state = game.Deserialize(snapshot.State);
            version = snapshot.Version;
            aiRng = Xoshiro256StarStar.FromState(snapshot.AiRng);
            lastClientSeq = snapshot.LastClientSeq;
            connections = snapshot.Connections;
            graceEnds = snapshot.GraceEnds;
            turnDeadline = snapshot.TurnDeadline;
            ended = snapshot.Ended;
        }

        // The move log is the truth: replay whatever the snapshot missed (a crash between the log write and the snapshot).
        foreach (var move in await sessions.ListMovesAsync(sessionId, (int)version, ct).ConfigureAwait(false))
        {
            var replayed = game.Apply(state, move.Seat, move.Action);
            if (!replayed.IsOk)
            {
                throw new InvalidOperationException($"Move {move.Seq} of session {sessionId} does not replay: {replayed.ErrorCode}.");
            }

            state = replayed.State!;
            version = move.Seq;
            turnDeadline = null;
        }

        var now = time.GetUtcNow();
        idleSince = now;
        if (ended is null && game.Outcome(state) is { } outcome)
        {
            ended = ToEvent(outcome);
            await sessions.RecordResultAsync(sessionId, outcome, ct).ConfigureAwait(false); // false when already recorded
        }
        else if (ended is null && live.Status is SessionStatuses.Finished or SessionStatuses.Abandoned)
        {
            ended = new MatchEndedEvent { Kind = "abandoned", Reason = "session_closed" };
        }

        if (ended is not null)
        {
            lingerUntil = now + Linger;
            turnDeadline = null;
            graceEnds.Clear();
        }
        else
        {
            ScheduleTurn(keepDeadline: true);
            if (snapshot is null && Online)
            {
                // A new online match: everyone gets the grace window to connect.
                foreach (var seat in live.Seats.Where(s => !s.IsAi))
                {
                    graceEnds[seat.Seat] = (now + Grace).ToUnixTimeMilliseconds();
                }
            }
        }

        await SaveAsync().ConfigureAwait(false);
        return true;
    }

    private async Task PumpInboxAsync(CancellationToken ct)
    {
        var db = redis.GetDatabase();
        while (!ct.IsCancellationRequested)
        {
            var items = await db.ListLeftPopAsync(RealtimeKeys.Inbox(sessionId), 64).ConfigureAwait(false);
            if (items is { Length: > 0 })
            {
                foreach (var item in items)
                {
                    if (TryParse(item) is { } message)
                    {
                        mailbox.Writer.TryWrite(message);
                    }
                }

                continue;
            }

            // Wait for a poke, polling once a second in case one was missed (pub/sub is fire and forget).
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(TimeSpan.FromSeconds(1));
            try
            {
                await pokes.Reader.ReadAsync(wait.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // The poll interval passed.
            }
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            while (mailbox.Reader.TryRead(out var message))
            {
                try
                {
                    await HandleAsync(message, ct).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // One bad message must not end the match for everyone; it is logged and answered.
                catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
                {
                    RealtimeLog.MessageFailed(log, ex, sessionId, message.Kind);
                    await SendErrorAsync(message, "server_error", "The server could not handle that message.").ConfigureAwait(false);
                }
            }

            if (await RunTimersAsync(ct).ConfigureAwait(false))
            {
                return; // the match unloaded itself
            }

            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var read = mailbox.Reader.WaitToReadAsync(wait.Token).AsTask();
            var due = NextDue();
            var delay = due is { } at
                ? Task.Delay(at - time.GetUtcNow() is var span && span > TimeSpan.Zero ? span : TimeSpan.Zero, time, wait.Token)
                : Task.Delay(Timeout.InfiniteTimeSpan, wait.Token);
            await Task.WhenAny(read, delay).ConfigureAwait(false);
            await wait.CancelAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
        }
    }

    private async Task HandleAsync(HostInbound message, CancellationToken ct)
    {
        if (message.Seat < 0 || message.Seat >= live.Seats.Count)
        {
            return;
        }

        switch (message.Kind)
        {
            case InboundKinds.Connect:
                await ConnectAsync(message.ConnId, message.Seat).ConfigureAwait(false);
                break;
            case InboundKinds.Alive:
                if (connections.TryGetValue(message.ConnId, out var known))
                {
                    known.LastSeen = Now;
                }
                else
                {
                    await ConnectAsync(message.ConnId, message.Seat, sendSnapshot: false).ConfigureAwait(false);
                }

                break;
            case InboundKinds.Disconnect:
                await DisconnectAsync(message.ConnId).ConfigureAwait(false);
                break;
            case InboundKinds.Resync:
                await SendSnapshotAsync(message.ConnId, message.Seat).ConfigureAwait(false);
                break;
            case InboundKinds.Action:
                await ActionAsync(message, ct).ConfigureAwait(false);
                break;
        }
    }

    private async Task ConnectAsync(string connId, int seat, bool sendSnapshot = true)
    {
        var seatWasAway = !SeatConnected(seat);
        connections[connId] = new SnapshotConnection { Seat = seat, LastSeen = Now };
        if (seatWasAway)
        {
            graceEnds.Remove(seat);
            if (ended is null)
            {
                ScheduleAi(); // vs AI resumes when its human is back
                await PublishAsync(new HostOutbound { T = MessageTypes.Presence, Payload = ToJson(new PresencePayload { Seat = seat, Connected = true }) }).ConfigureAwait(false);
            }

            await SaveAsync().ConfigureAwait(false);
        }

        if (sendSnapshot)
        {
            await SendSnapshotAsync(connId, seat).ConfigureAwait(false);
        }
    }

    private async Task DisconnectAsync(string connId)
    {
        if (!connections.Remove(connId, out var gone))
        {
            return;
        }

        if (connections.Count == 0)
        {
            idleSince = time.GetUtcNow();
        }

        if (!SeatConnected(gone.Seat) && ended is null)
        {
            long? graceEnd = null;
            if (Online)
            {
                graceEnd = (time.GetUtcNow() + Grace).ToUnixTimeMilliseconds();
                graceEnds[gone.Seat] = graceEnd.Value;
            }

            await PublishAsync(new HostOutbound
            {
                T = MessageTypes.Presence,
                Payload = ToJson(new PresencePayload { Seat = gone.Seat, Connected = false, GraceEndsAt = graceEnd }),
            }).ConfigureAwait(false);
        }

        await SaveAsync().ConfigureAwait(false);
    }

    private async Task ActionAsync(HostInbound message, CancellationToken ct)
    {
        if (ended is not null)
        {
            await SendErrorAsync(message, "match_over", "The match is over.").ConfigureAwait(false);
            return;
        }

        if (message.Seq <= lastClientSeq.GetValueOrDefault(message.Seat))
        {
            // A resend of an action already handled (after a reconnect): show where the game is instead of applying twice.
            await SendSnapshotAsync(message.ConnId, message.Seat).ConfigureAwait(false);
            return;
        }

        lastClientSeq[message.Seat] = message.Seq;
        if (message.Payload is not { } action)
        {
            await SendErrorAsync(message, "bad_action", "An action needs a payload.").ConfigureAwait(false);
            return;
        }

        var result = await CommitAsync(message.Seat, action, message.Seq, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            await SendErrorAsync(message, result.ErrorCode!, result.ErrorMessage ?? result.ErrorCode!).ConfigureAwait(false);
        }
    }

    /// <summary>Applies, logs, snapshots and broadcasts one action. The only place the state changes.</summary>
    private async Task<MatchApplyResult> CommitAsync(int seat, JsonElement action, long? appliedSeq, CancellationToken ct)
    {
        var result = game.Apply(state, seat, action);
        if (!result.IsOk)
        {
            return result;
        }

        var next = version + 1;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
            var appended = await sessions.AppendMoveAsync(sessionId, (int)next, seat, action, next, ct).ConfigureAwait(false);
            if (appended != AppendMoveResult.Appended)
            {
                // Another host wrote this seq (this one lost its lease) or the session closed: stop being the authority.
                RealtimeLog.LostAuthority(log, sessionId, appended.ToString());
                Cancel();
                return new MatchApplyResult(null, "server_moved", "The match moved to another server; reconnect.");
            }

            state = result.State!;
            version = next;
            if (game.Outcome(state) is { } outcome)
            {
                ended = ToEvent(outcome);
                await sessions.RecordResultAsync(sessionId, outcome, ct).ConfigureAwait(false);
                turnDeadline = null;
                aiDue = null;
                graceEnds.Clear();
                lingerUntil = time.GetUtcNow() + Linger;
            }
            else
            {
                ScheduleTurn(keepDeadline: false);
            }
        }

        await SaveAsync().ConfigureAwait(false);
        var turnSeat = ended is null ? game.ActingSeat(state) : null;
        for (var s = 0; s < live.Seats.Count; s++)
        {
            await PublishAsync(new HostOutbound
            {
                Seat = s,
                T = MessageTypes.State,
                StateVersion = version,
                Payload = ToJson(new StateUpdate<JsonElement>
                {
                    State = game.View(state, s),
                    AppliedSeq = s == seat ? appliedSeq : null,
                    TurnSeat = turnSeat,
                    TurnDeadline = turnDeadline,
                }),
            }).ConfigureAwait(false);
        }

        if (ended is not null)
        {
            await PublishAsync(new HostOutbound { T = MessageTypes.Ended, StateVersion = version, Payload = ToJson(ended) }).ConfigureAwait(false);
            RealtimeLog.MatchEnded(log, sessionId, ended.Kind, ended.Reason, version);
        }

        return result;
    }

    /// <summary>Runs whatever is due. True when the host should unload.</summary>
    private async Task<bool> RunTimersAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var nowMs = now.ToUnixTimeMilliseconds();

        // Connections that stopped sending heartbeats (their instance died) count as closed.
        var staleBefore = nowMs - (long)TimeSpan.FromSeconds(options.HeartbeatSeconds + options.PongTimeoutSeconds).TotalMilliseconds;
        foreach (var stale in connections.Where(c => c.Value.LastSeen < staleBefore).Select(c => c.Key).ToList())
        {
            await DisconnectAsync(stale).ConfigureAwait(false);
        }

        if (ended is not null)
        {
            return lingerUntil <= now;
        }

        foreach (var (seat, end) in graceEnds.Where(g => g.Value <= nowMs).ToList())
        {
            graceEnds.Remove(seat);
            RealtimeLog.GraceExpired(log, sessionId, seat);
            await CommitAsync(seat, game.ForfeitAction, null, ct).ConfigureAwait(false); // rejected (not_playing) if the seat already finished
            if (ended is not null)
            {
                return false;
            }
        }

        if (turnDeadline <= nowMs && game.ActingSeat(state) is { } late)
        {
            turnDeadline = null;
            await CommitAsync(late, game.TimeoutAction(state, late), null, ct).ConfigureAwait(false);
        }
        else if (aiDue <= now && connections.Count > 0 && game.ActingSeat(state) is { } ai && live.Seats[ai].IsAi)
        {
            aiDue = null;
            await CommitAsync(ai, game.AiAction(state, ai, live.Seats[ai].AiLevel ?? 1, aiRng), null, ct).ConfigureAwait(false);
        }

        return !Online && connections.Count == 0 && idleSince + TimeSpan.FromMinutes(options.IdleUnloadMinutes) <= now;
    }

    private DateTimeOffset? NextDue()
    {
        var candidates = new List<DateTimeOffset>();
        if (ended is not null)
        {
            if (lingerUntil is { } linger)
            {
                candidates.Add(linger);
            }
        }
        else
        {
            if (turnDeadline is { } turn)
            {
                candidates.Add(DateTimeOffset.FromUnixTimeMilliseconds(turn));
            }

            if (aiDue is { } ai && connections.Count > 0)
            {
                candidates.Add(ai);
            }

            candidates.AddRange(graceEnds.Values.Select(DateTimeOffset.FromUnixTimeMilliseconds));
            if (!Online && connections.Count == 0)
            {
                candidates.Add(idleSince + TimeSpan.FromMinutes(options.IdleUnloadMinutes));
            }
        }

        var stale = TimeSpan.FromSeconds(options.HeartbeatSeconds + options.PongTimeoutSeconds);
        candidates.AddRange(connections.Values.Select(c => DateTimeOffset.FromUnixTimeMilliseconds(c.LastSeen) + stale + TimeSpan.FromMilliseconds(1)));
        return candidates.Count == 0 ? null : candidates.Min();
    }

    /// <summary>Sets the clock for the seat to act: a turn deadline for humans online, an AI delay for AI seats.</summary>
    private void ScheduleTurn(bool keepDeadline)
    {
        var acting = game.ActingSeat(state);
        if (!keepDeadline || turnDeadline is null)
        {
            turnDeadline = Online && acting is { } human && !live.Seats[human].IsAi
                ? (time.GetUtcNow() + TimeSpan.FromSeconds(options.TurnSeconds)).ToUnixTimeMilliseconds()
                : null;
        }

        ScheduleAi();
    }

    private void ScheduleAi() =>
        aiDue = game.ActingSeat(state) is { } seat && live.Seats[seat].IsAi
            ? time.GetUtcNow() + TimeSpan.FromMilliseconds(options.AiDelayMilliseconds)
            : null;

    private bool SeatConnected(int seat) => connections.Values.Any(c => c.Seat == seat);

    private async Task SendSnapshotAsync(string connId, int seat)
    {
        await PublishAsync(new HostOutbound
        {
            ConnId = connId,
            T = MessageTypes.Snapshot,
            StateVersion = version,
            Payload = ToJson(new StateUpdate<JsonElement>
            {
                State = game.View(state, seat),
                TurnSeat = ended is null ? game.ActingSeat(state) : null,
                TurnDeadline = turnDeadline,
            }),
        }).ConfigureAwait(false);

        for (var other = 0; other < live.Seats.Count; other++)
        {
            if (other == seat || live.Seats[other].IsAi)
            {
                continue;
            }

            await PublishAsync(new HostOutbound
            {
                ConnId = connId,
                T = MessageTypes.Presence,
                Payload = ToJson(new PresencePayload
                {
                    Seat = other,
                    Connected = SeatConnected(other),
                    GraceEndsAt = graceEnds.TryGetValue(other, out var end) ? end : null,
                }),
            }).ConfigureAwait(false);
        }

        if (ended is not null)
        {
            await PublishAsync(new HostOutbound { ConnId = connId, T = MessageTypes.Ended, StateVersion = version, Payload = ToJson(ended) }).ConfigureAwait(false);
        }
    }

    private Task SendErrorAsync(HostInbound to, string code, string message) =>
        PublishAsync(new HostOutbound { ConnId = to.ConnId, T = MessageTypes.Error, Payload = ToJson(new ErrorPayload { Code = code, Message = message, Seq = to.Seq }) });

    private async Task PublishAsync(HostOutbound message) =>
        await redis.GetSubscriber().PublishAsync(RealtimeKeys.Outbox(sessionId), JsonSerializer.Serialize(message, Json)).ConfigureAwait(false);

    private async Task SaveAsync()
    {
        var snapshot = new MatchSnapshot
        {
            Version = version,
            State = game.Serialize(state),
            LastClientSeq = lastClientSeq,
            Connections = connections,
            GraceEnds = graceEnds,
            TurnDeadline = turnDeadline,
            AiRng = aiRng.GetState(),
            Ended = ended,
        };
        var db = redis.GetDatabase();
        var ttl = ended is null ? TimeSpan.FromHours(options.SnapshotHours) : SnapshotAfterEnd;
        await db.StringSetAsync(RealtimeKeys.Snapshot(sessionId), JsonSerializer.Serialize(snapshot, Json), ttl).ConfigureAwait(false);

        // The safety-net job finds matches whose next deadline passed while no instance hosted them.
        var deadlines = graceEnds.Values.Concat(turnDeadline is { } t ? [t] : []).ToList();
        if (ended is null && deadlines.Count > 0)
        {
            await db.SortedSetAddAsync(RealtimeKeys.Deadlines, sessionId.ToString(), deadlines.Min()).ConfigureAwait(false);
        }
        else
        {
            await db.SortedSetRemoveAsync(RealtimeKeys.Deadlines, sessionId.ToString()).ConfigureAwait(false);
        }
    }

    private async Task RenewLeaseAsync(CancellationTokenSource host, CancellationToken ct)
    {
        var every = TimeSpan.FromMilliseconds(options.LeaseSeconds * 1000 / 3);
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(every, ct).ConfigureAwait(false); // wall clock: the lease TTL in Redis is wall clock too
            if (!await registry.RenewLeaseAsync(sessionId).ConfigureAwait(false))
            {
                RealtimeLog.LostAuthority(log, sessionId, "lease");
                releaseLease = false; // it is someone else's now
                await host.CancelAsync().ConfigureAwait(false);
                return;
            }
        }
    }

    private static MatchEndedEvent ToEvent(MatchOutcome outcome) => new()
    {
        Kind = outcome.Outcome,
        WinningSeats = [.. outcome.WinningSeats],
        Placements = [.. outcome.Placements.OrderBy(p => p.Value).Select(p => p.Key)],
        Reason = outcome.Reason,
    };

    private static JsonElement ToJson<T>(T value) => JsonSerializer.SerializeToElement(value, Json);

    private static HostInbound? TryParse(RedisValue item)
    {
        try
        {
            return JsonSerializer.Deserialize<HostInbound>(item.ToString(), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task Quietly(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on stop.
        }
    }
}
