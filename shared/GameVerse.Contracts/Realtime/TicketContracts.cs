namespace GameVerse.Contracts.Realtime
{
    /// <summary>
    /// Asks for a single-use WebSocket ticket (valid 30 seconds, CLAUDE.md section 7). Set exactly one target: a session to
    /// play in (<c>/ws/play/{sessionId}</c>) or a private room's lobby (<c>/ws/room/{code}</c>). The response is a
    /// <see cref="GameVerse.Contracts.Session.RealtimeInfo"/>.
    /// </summary>
    public class TicketRequest
    {
        /// <summary>Gets or sets the session to play in.</summary>
        public string? SessionId { get; set; }

        /// <summary>Gets or sets the private room code whose lobby to follow.</summary>
        public string? RoomCode { get; set; }
    }
}
