using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace GameVerse.Platform.Realtime;

/// <summary>
/// The safety net behind the in-process turn timers (CLAUDE.md section 3: Hangfire backs up turn timers). Runs every
/// minute on the worker. A live match normally keeps itself moving: its host's timers auto-play, forfeit and end it, and
/// connections restart a host that died. When every player is gone and the hosting instance died, nothing would. This job
/// finds matches whose next deadline has passed (the <c>rt:deadlines</c> index) without a lease, and starts them here, so
/// timeouts and forfeits still happen and the match still ends.
/// </summary>
public sealed class MatchWatchdog(IServiceProvider services)
{
    /// <summary>The Hangfire recurring job id.</summary>
    public const string JobId = "realtime-match-watchdog";

    /// <summary>Starts a host for each overdue, unhosted match. Returns how many it started.</summary>
    public async Task<int> RunAsync()
    {
        var registry = services.GetRequiredService<MatchHostRegistry>();
        var redis = services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().ToUnixTimeMilliseconds();

        var overdue = await redis.SortedSetRangeByScoreAsync(RealtimeKeys.Deadlines, double.NegativeInfinity, now, take: 500).ConfigureAwait(false);
        var started = 0;
        foreach (var member in overdue)
        {
            if (!Guid.TryParse(member.ToString(), out var sessionId) || await redis.KeyExistsAsync(RealtimeKeys.Lease(sessionId)).ConfigureAwait(false))
            {
                continue; // hosted somewhere: its own timers handle it
            }

            await registry.EnsureHostAsync(sessionId).ConfigureAwait(false);
            if (registry.IsHosting(sessionId))
            {
                started++;
            }
        }

        return started;
    }
}
