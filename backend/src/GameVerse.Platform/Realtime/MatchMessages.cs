using System.Text.Json;

namespace GameVerse.Platform.Realtime;

/// <summary>Kinds of <see cref="HostInbound"/>.</summary>
internal static class InboundKinds
{
    /// <summary>A connection opened; the host answers it with a snapshot.</summary>
    public const string Connect = "connect";

    /// <summary>A connection is still open (sent with every heartbeat).</summary>
    public const string Alive = "alive";

    /// <summary>A connection closed.</summary>
    public const string Disconnect = "disconnect";

    /// <summary>A player intent.</summary>
    public const string Action = "action";

    /// <summary>A connection asks for a snapshot.</summary>
    public const string Resync = "resync";
}

/// <summary>A message from a connection (on any instance) to the match host, queued in the match's Redis inbox.</summary>
internal sealed class HostInbound
{
    public string Kind { get; set; } = string.Empty;

    public string ConnId { get; set; } = string.Empty;

    public int Seat { get; set; }

    /// <summary>The client's envelope seq (actions only).</summary>
    public long Seq { get; set; }

    public JsonElement? Payload { get; set; }
}

/// <summary>
/// A message from the match host to the connections, published on the match's outbox channel. Each connection forwards
/// the ones addressed to it: everyone (no seat, no connection), its seat, or its own connection id.
/// </summary>
internal sealed class HostOutbound
{
    public int? Seat { get; set; }

    public string? ConnId { get; set; }

    /// <summary>The envelope type (GameVerse.Contracts.Realtime.MessageTypes).</summary>
    public string T { get; set; } = string.Empty;

    public long? StateVersion { get; set; }

    public JsonElement? Payload { get; set; }

    public bool IsFor(string connId, int seat) => (ConnId is null || ConnId == connId) && (Seat is null || Seat == seat);
}

/// <summary>
/// A live match in Redis, written after every change, so any instance can resume it. The move log in Postgres stays the
/// source of truth: a resume replays any logged moves newer than <see cref="Version"/>.
/// </summary>
internal sealed class MatchSnapshot
{
    /// <summary>Accepted actions so far (= the last move-log seq = the state version clients see).</summary>
    public long Version { get; set; }

    /// <summary>The game's full state, dice PRNG included (IMatchGame.Serialize). Server-only.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Highest client seq seen per seat: a resent action is never applied twice.</summary>
    public Dictionary<int, long> LastClientSeq { get; set; } = [];

    /// <summary>Open connections: id to seat and last sign of life (Unix ms).</summary>
    public Dictionary<string, SnapshotConnection> Connections { get; set; } = [];

    /// <summary>Disconnected seats and when they forfeit (Unix ms).</summary>
    public Dictionary<int, long> GraceEnds { get; set; } = [];

    /// <summary>When the acting seat's turn clock runs out (Unix ms), if it has one.</summary>
    public long? TurnDeadline { get; set; }

    /// <summary>The AI seats' own PRNG (never the dice).</summary>
    public ulong[] AiRng { get; set; } = [];

    /// <summary>The result, once the match is over.</summary>
    public Contracts.Events.MatchEndedEvent? Ended { get; set; }
}

internal sealed class SnapshotConnection
{
    public int Seat { get; set; }

    public long LastSeen { get; set; }
}
