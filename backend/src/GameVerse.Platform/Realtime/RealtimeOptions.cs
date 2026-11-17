using StackExchange.Redis;

namespace GameVerse.Platform.Realtime;

/// <summary>Realtime settings, bound from GameVerse:Realtime (ADR-0009). Defaults are the production values.</summary>
public sealed class RealtimeOptions
{
    public const string Section = "GameVerse:Realtime";

    /// <summary>
    /// Public base URL clients reach the API on, e.g. https://api.gameverse.app (wss:// is derived). Empty: use the
    /// request's own scheme and host, which is right for local development.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>Lifetime of a single-use socket ticket (CLAUDE.md section 7: 30 seconds).</summary>
    public int TicketSeconds { get; set; } = 30;

    /// <summary>Largest accepted client message.</summary>
    public int MaxMessageBytes { get; set; } = 4096;

    /// <summary>Sustained client messages per second per connection (a token bucket of the same size).</summary>
    public int MessagesPerSecond { get; set; } = 20;

    /// <summary>The server sends an application ping this often (CLAUDE.md 4.5: 15 seconds).</summary>
    public int HeartbeatSeconds { get; set; } = 15;

    /// <summary>A connection with no pong for this long is closed (CLAUDE.md 4.5: 45 seconds).</summary>
    public int PongTimeoutSeconds { get; set; } = 45;

    /// <summary>Time a human seat has for each decision in online modes before the server plays for it.</summary>
    public int TurnSeconds { get; set; } = 15;

    /// <summary>Disconnect grace before a forfeit in casual online modes (private rooms).</summary>
    public int GraceCasualSeconds { get; set; } = 60;

    /// <summary>Disconnect grace before a forfeit in ranked modes.</summary>
    public int GraceRankedSeconds { get; set; } = 30;

    /// <summary>Pause before an AI seat acts, so clients can show each step. 0 in tests.</summary>
    public int AiDelayMilliseconds { get; set; } = 600;

    /// <summary>A vs AI match with no human connected unloads after this long; its snapshot stays for resuming.</summary>
    public int IdleUnloadMinutes { get; set; } = 10;

    /// <summary>Match ownership lease in Redis. A dead instance's matches move to another within this time.</summary>
    public int LeaseSeconds { get; set; } = 10;

    /// <summary>How long a live match's snapshot is kept in Redis after its last change.</summary>
    public int SnapshotHours { get; set; } = 24;
}

/// <summary>Redis keys and channels of the Realtime module. Everything is prefixed "rt:".</summary>
internal static class RealtimeKeys
{
    public static RedisKey Ticket(string ticketHash) => "rt:ticket:" + ticketHash;

    public static RedisKey Lease(Guid sessionId) => "rt:match:" + sessionId + ":lease";

    public static RedisKey Snapshot(Guid sessionId) => "rt:match:" + sessionId + ":snap";

    /// <summary>Client intents waiting for the match host (a list: survives until a host drains it).</summary>
    public static RedisKey Inbox(Guid sessionId) => "rt:match:" + sessionId + ":inbox";

    /// <summary>Overdue-match index for the safety-net job: member session id, score the next deadline (Unix ms).</summary>
    public static RedisKey Deadlines => "rt:deadlines";

    /// <summary>Tells the host its inbox has something.</summary>
    public static RedisChannel InboxPoke(Guid sessionId) => RedisChannel.Literal("rt:match:" + sessionId + ":in");

    /// <summary>Host output for the connections, on every instance.</summary>
    public static RedisChannel Outbox(Guid sessionId) => RedisChannel.Literal("rt:match:" + sessionId + ":out");
}
