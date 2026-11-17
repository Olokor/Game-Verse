using System.Collections.Concurrent;
using GameVerse.Platform.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace GameVerse.Platform.Realtime;

/// <summary>
/// The match hosts running on this instance, and the Redis lease that makes each match run on exactly one instance.
/// A lease is <c>SET NX</c> with a TTL the host renews; a crashed instance's leases expire and the next connection (or the
/// safety-net job) starts the match elsewhere. Also an <see cref="IHostedService"/>, so shutdown stops hosts cleanly.
/// </summary>
internal sealed class MatchHostRegistry(
    IServiceScopeFactory scopes,
    IConnectionMultiplexer redis,
    IMatchGames games,
    RealtimeOptions options,
    TimeProvider time,
    ILoggerFactory loggers) : IHostedService, IDisposable
{
    // Compare-and-act scripts: only the lease holder may extend or delete it.
    private const string RenewScript = "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('pexpire', KEYS[1], ARGV[2]) else return 0 end";
    private const string ReleaseScript = "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    private readonly ConcurrentDictionary<Guid, (MatchHost Host, Task Run)> hosts = new();
    private readonly CancellationTokenSource shutdown = new();
    private int disposed;

    /// <summary>This process's id in leases.</summary>
    public string InstanceId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>Starts the match here unless some instance (this one included) already holds its lease.</summary>
    public async Task EnsureHostAsync(Guid sessionId)
    {
        if (hosts.ContainsKey(sessionId) || Volatile.Read(ref disposed) == 1 || shutdown.IsCancellationRequested)
        {
            return;
        }

        var acquired = await redis.GetDatabase()
            .StringSetAsync(RealtimeKeys.Lease(sessionId), InstanceId, TimeSpan.FromSeconds(options.LeaseSeconds), When.NotExists)
            .ConfigureAwait(false);
        if (!acquired)
        {
            return;
        }

        var host = new MatchHost(sessionId, InstanceId, this, scopes, redis, games, options, time, loggers.CreateLogger<MatchHost>());
        var run = Task.Run(() => host.RunAsync(shutdown.Token));
        if (!hosts.TryAdd(sessionId, (host, run)))
        {
            host.Stop(); // cannot happen with the lease held, but never run two hosts for one match in one process
        }
    }

    public bool IsHosting(Guid sessionId) => hosts.ContainsKey(sessionId);

    public async Task<bool> RenewLeaseAsync(Guid sessionId)
    {
        var renewed = await redis.GetDatabase().ScriptEvaluateAsync(
            RenewScript,
            [RealtimeKeys.Lease(sessionId)],
            [InstanceId, (long)TimeSpan.FromSeconds(options.LeaseSeconds).TotalMilliseconds]).ConfigureAwait(false);
        return (long)renewed == 1;
    }

    public Task ReleaseLeaseAsync(Guid sessionId) =>
        redis.GetDatabase().ScriptEvaluateAsync(ReleaseScript, [RealtimeKeys.Lease(sessionId)], [InstanceId]);

    public void Remove(MatchHost host)
    {
        if (hosts.TryGetValue(host.SessionId, out var entry) && ReferenceEquals(entry.Host, host))
        {
            hosts.TryRemove(host.SessionId, out _);
        }
    }

    /// <summary>Kills every host as a crash would (leases left to expire). For resume tests only.</summary>
    internal async Task AbortAllAsync()
    {
        var running = hosts.Values.ToList();
        foreach (var (host, _) in running)
        {
            host.Abort();
        }

        await Task.WhenAll(running.Select(r => r.Run)).ConfigureAwait(false);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // The container may dispose this singleton before the host stops it (e.g. a host that never fully started).
        if (Volatile.Read(ref disposed) == 0)
        {
            await shutdown.CancelAsync().ConfigureAwait(false);
        }

        await Task.WhenAll(hosts.Values.Select(r => r.Run)).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            shutdown.Cancel(); // stop any host still running before the token goes away
            shutdown.Dispose();
        }
    }
}
