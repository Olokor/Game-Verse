using System.Globalization;
using System.Text.Json;
using StackExchange.Redis;

namespace GameVerse.Platform.Matchmaking;

/// <summary>Matchmaking timings (GameVerse:Matchmaking). Defaults are the production values (ADR-0010).</summary>
public sealed class MatchmakingOptions
{
    public const string Section = "GameVerse:Matchmaking";

    /// <summary>Ranked: how far apart two ratings may be when a player starts searching.</summary>
    public int BandStart { get; set; } = 100;

    /// <summary>Ranked: how much the band widens per step of waiting.</summary>
    public int BandStep { get; set; } = 50;

    public int BandStepSeconds { get; set; } = 5;

    /// <summary>Ranked: the widest band while waiting less than <see cref="AnyoneAfterSeconds"/>.</summary>
    public int BandMax { get; set; } = 500;

    /// <summary>Ranked: after this long any rating is accepted, so a small player pool still gets games.</summary>
    public int AnyoneAfterSeconds { get; set; } = 60;

    /// <summary>Casual: after this long the oldest player's game fills its empty seats with AI.</summary>
    public int AiFillSeconds { get; set; } = 20;

    /// <summary>Casual: the level of fill-in AI seats.</summary>
    public int AiLevel { get; set; } = 2;

    /// <summary>The matcher runs this often on every API instance (one instance per queue per tick).</summary>
    public int TickMilliseconds { get; set; } = 1000;

    /// <summary>Queue joins per client IP per minute (CLAUDE.md section 7).</summary>
    public int JoinsPerMinute { get; set; } = 30;

    /// <summary>How long a "matched" status stays readable after the match.</summary>
    public int MatchedMinutes { get; set; } = 10;

    /// <summary>The rating band for a ranked player who has waited this long; null means any rating.</summary>
    public int? Band(TimeSpan waited) =>
        waited.TotalSeconds >= AnyoneAfterSeconds
            ? null
            : Math.Min(BandMax, BandStart + ((int)(waited.TotalSeconds / BandStepSeconds) * BandStep));
}

/// <summary>A player waiting in a queue.</summary>
internal sealed class QueueEntry
{
    public Guid UserId { get; set; }

    /// <summary>The queue's Redis key, e.g. mm:q:ludo:ranked:2.</summary>
    public string Queue { get; set; } = string.Empty;

    public string GameId { get; set; } = string.Empty;

    public string ModeKey { get; set; } = string.Empty;

    public string ModeKind { get; set; } = string.Empty;

    public int PlayerCount { get; set; }

    public double Rating { get; set; }

    /// <summary>Unix milliseconds on the game clock (TimeProvider).</summary>
    public long JoinedAt { get; set; }
}

/// <summary>A finished search, readable by the player's next poll.</summary>
internal sealed class MatchedTicket
{
    public Guid SessionId { get; set; }

    public string GameId { get; set; } = string.Empty;

    public string ModeKey { get; set; } = string.Empty;

    public int PlayerCount { get; set; }

    public int WaitedSeconds { get; set; }
}

/// <summary>
/// The Redis side of matchmaking. A queue is a sorted set of user ids by join time; each waiting player also has an entry
/// (which queue, rating, join time). Changes that must not interleave (join, cancel, claim a group) are atomic.
/// </summary>
internal sealed class QueueStore(IConnectionMultiplexer redis)
{
    /// <summary>Every queue that may hold players, so the matcher knows where to look.</summary>
    public static readonly RedisKey Queues = "mm:queues";

    // Removes the whole group only if every member is still waiting: a cancel or another matcher can never split a game.
    private const string ClaimScript = """
        for i, user in ipairs(ARGV) do
          if not redis.call('zscore', KEYS[1], user) then return 0 end
        end
        for i, user in ipairs(ARGV) do
          redis.call('zrem', KEYS[1], user)
          redis.call('del', 'mm:e:' .. user)
        end
        return 1
        """;

    private const string CancelScript = """
        if redis.call('zrem', KEYS[1], ARGV[1]) == 1 then
          redis.call('del', KEYS[2])
          return 1
        end
        return 0
        """;

    // Forget an empty queue, unless a join slipped in (joins add to the queue and the index in one transaction).
    private const string ForgetIfEmptyScript = """
        if redis.call('zcard', KEYS[2]) == 0 then return redis.call('srem', KEYS[1], ARGV[1]) end
        return 0
        """;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Longest a search can last; casual and ranked both match long before (AI fill, the any-rating band).</summary>
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromHours(1);

    private IDatabase Db => redis.GetDatabase();

    public static string QueueKey(string gameId, string modeKey, int players) =>
        "mm:q:" + gameId + ":" + modeKey + ":" + players.ToString(CultureInfo.InvariantCulture);

    private static RedisKey EntryKey(Guid userId) => "mm:e:" + userId;

    private static RedisKey MatchedKey(Guid userId) => "mm:m:" + userId;

    /// <summary>Adds the player unless they are already waiting somewhere. False when they are.</summary>
    public async Task<bool> JoinAsync(QueueEntry entry)
    {
        // The entry expires on its own, so a crash between these steps can never lock a player out of matchmaking.
        if (!await Db.StringSetAsync(EntryKey(entry.UserId), JsonSerializer.Serialize(entry, Json), EntryLifetime, When.NotExists).ConfigureAwait(false))
        {
            return false;
        }

        await Db.KeyDeleteAsync(MatchedKey(entry.UserId)).ConfigureAwait(false); // a new search replaces an old result
        // Single-node Redis: the claim script also touches entry keys it builds itself (mm:e:{user}).
        var join = Db.CreateTransaction();
        _ = join.SortedSetAddAsync(entry.Queue, entry.UserId.ToString(), entry.JoinedAt);
        _ = join.SetAddAsync(Queues, entry.Queue);
        await join.ExecuteAsync().ConfigureAwait(false);
        return true;
    }

    public async Task<QueueEntry?> GetEntryAsync(Guid userId)
    {
        var json = await Db.StringGetAsync(EntryKey(userId)).ConfigureAwait(false);
        return json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<QueueEntry>(json.ToString(), Json);
    }

    public async Task<MatchedTicket?> GetMatchedAsync(Guid userId)
    {
        var json = await Db.StringGetAsync(MatchedKey(userId)).ConfigureAwait(false);
        return json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<MatchedTicket>(json.ToString(), Json);
    }

    public async Task<bool> CancelAsync(QueueEntry entry) =>
        (long)await Db.ScriptEvaluateAsync(CancelScript, [entry.Queue, EntryKey(entry.UserId)], [entry.UserId.ToString()]).ConfigureAwait(false) == 1;

    public async Task<IReadOnlyList<string>> QueuesAsync() =>
        (await Db.SetMembersAsync(Queues).ConfigureAwait(false)).Select(q => q.ToString()).ToList();

    /// <summary>Everyone waiting in a queue, oldest first.</summary>
    public async Task<IReadOnlyList<QueueEntry>> WaitingAsync(string queue)
    {
        var users = await Db.SortedSetRangeByRankAsync(queue).ConfigureAwait(false);
        if (users.Length == 0)
        {
            return [];
        }

        var entries = await Db.StringGetAsync(users.Select(u => EntryKey(Guid.Parse(u.ToString()))).ToArray()).ConfigureAwait(false);
        var orphans = users.Where((_, i) => entries[i].IsNullOrEmpty).ToArray();
        if (orphans.Length > 0)
        {
            await Db.SortedSetRemoveAsync(queue, orphans).ConfigureAwait(false); // their entry expired: nobody is waiting there
        }

        return entries.Where(e => !e.IsNullOrEmpty).Select(e => JsonSerializer.Deserialize<QueueEntry>(e.ToString(), Json)!).OrderBy(e => e.JoinedAt).ToList();
    }

    /// <summary>Takes these players out of the queue together, or none of them. True when this caller got them.</summary>
    public async Task<bool> ClaimAsync(string queue, IReadOnlyList<Guid> users) =>
        (long)await Db.ScriptEvaluateAsync(ClaimScript, [queue], users.Select(u => (RedisValue)u.ToString()).ToArray()).ConfigureAwait(false) == 1;

    /// <summary>Puts claimed players back, keeping their places (the session could not be created).</summary>
    public async Task RestoreAsync(IEnumerable<QueueEntry> entries)
    {
        foreach (var entry in entries)
        {
            await Db.StringSetAsync(EntryKey(entry.UserId), JsonSerializer.Serialize(entry, Json)).ConfigureAwait(false);
            await Db.SortedSetAddAsync(entry.Queue, entry.UserId.ToString(), entry.JoinedAt).ConfigureAwait(false);
            await Db.SetAddAsync(Queues, entry.Queue).ConfigureAwait(false);
        }
    }

    public Task MarkMatchedAsync(Guid userId, MatchedTicket ticket, TimeSpan keep) =>
        Db.StringSetAsync(MatchedKey(userId), JsonSerializer.Serialize(ticket, Json), keep);

    public Task ForgetIfEmptyAsync(string queue) =>
        Db.ScriptEvaluateAsync(ForgetIfEmptyScript, [Queues, queue], [queue]);

    /// <summary>One matcher per queue per tick, across instances. Returns the lock token, or null when another holds it.</summary>
    public async Task<string?> TryLockAsync(string queue, TimeSpan hold)
    {
        var token = Guid.NewGuid().ToString("N");
        return await Db.StringSetAsync("mm:lock:" + queue, token, hold, When.NotExists).ConfigureAwait(false) ? token : null;
    }

    public Task UnlockAsync(string queue, string token) =>
        Db.ScriptEvaluateAsync(
            "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end",
            ["mm:lock:" + queue],
            [token]);
}
