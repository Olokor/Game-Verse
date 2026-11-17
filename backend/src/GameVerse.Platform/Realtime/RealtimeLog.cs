using Microsoft.Extensions.Logging;

namespace GameVerse.Platform.Realtime;

/// <summary>Source-generated log messages of the Realtime module. Never log tickets, tokens or game secrets.</summary>
internal static partial class RealtimeLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "GV_MATCH host started for {SessionId} on {InstanceId} at version {Version}")]
    public static partial void HostStarted(ILogger logger, Guid sessionId, string instanceId, long version);

    [LoggerMessage(Level = LogLevel.Information, Message = "GV_MATCH host stopped for {SessionId} (lease released: {Released})")]
    public static partial void HostStopped(ILogger logger, Guid sessionId, bool released);

    [LoggerMessage(Level = LogLevel.Error, Message = "GV_MATCH host failed for {SessionId}")]
    public static partial void HostFailed(ILogger logger, Exception exception, Guid sessionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "GV_MATCH {SessionId} failed to handle a {Kind} message")]
    public static partial void MessageFailed(ILogger logger, Exception exception, Guid sessionId, string kind);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GV_MATCH host for {SessionId} lost authority ({Reason}) and stopped")]
    public static partial void LostAuthority(ILogger logger, Guid sessionId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "GV_MATCH {SessionId} ended: {Kind} ({Reason}) after {Version} actions")]
    public static partial void MatchEnded(ILogger logger, Guid sessionId, string kind, string reason, long version);

    [LoggerMessage(Level = LogLevel.Information, Message = "GV_MATCH {SessionId} seat {Seat} did not come back in time and forfeits")]
    public static partial void GraceExpired(ILogger logger, Guid sessionId, int seat);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GV_SOCKET dropped malformed envelope on {SessionId}: {Reason}")]
    public static partial void MalformedEnvelope(ILogger logger, string sessionId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "GV_SOCKET lobby socket for room {Code} closed")]
    public static partial void LobbyClosed(ILogger logger, string code);

    [LoggerMessage(Level = LogLevel.Information, Message = "GV_SOCKET closed {SessionId} seat {Seat}: {Reason}")]
    public static partial void SocketClosed(ILogger logger, Guid sessionId, int seat, string reason);
}
