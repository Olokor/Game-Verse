namespace GameVerse.Contracts.Realtime
{
    /// <summary>
    /// Header of every realtime message (CLAUDE.md 4.5). Receivers deserialize this first, read
    /// <see cref="T"/>, then deserialize the full <see cref="Envelope{TPayload}"/> with the right payload type.
    /// </summary>
    public class EnvelopeHeader
    {
        /// <summary>Gets or sets the message type, one of <see cref="MessageTypes"/>. Serialized as "t".</summary>
        public string T { get; set; } = string.Empty;

        /// <summary>Gets or sets the sender's sequence number, increasing per connection.</summary>
        public long Seq { get; set; }

        /// <summary>Gets or sets the session id.</summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Gets or sets the server state version on state-carrying messages; clients drop anything older than they have.</summary>
        public long? StateVersion { get; set; }
    }

    /// <summary>A realtime message with a typed payload.</summary>
    /// <typeparam name="TPayload">Payload type for this message type.</typeparam>
    public class Envelope<TPayload> : EnvelopeHeader
    {
        /// <summary>Gets or sets the payload.</summary>
        public TPayload Payload { get; set; } = default!;
    }

    /// <summary>Values of <see cref="EnvelopeHeader.T"/>.</summary>
    public static class MessageTypes
    {
        /// <summary>Client to server: a player intent. Payload is the game's action type.</summary>
        public const string Action = "action";

        /// <summary>Server to client: state after an applied action. Payload <see cref="StateUpdate{TState}"/>.</summary>
        public const string State = "state";

        /// <summary>Server to client: full state after (re)connect. Payload <see cref="StateUpdate{TState}"/>.</summary>
        public const string Snapshot = "snapshot";

        /// <summary>Client to server: ask for a snapshot. Payload <see cref="ResyncRequest"/>.</summary>
        public const string Resync = "resync";

        /// <summary>Server to client: a rejected action or protocol error. Payload <see cref="ErrorPayload"/>.</summary>
        public const string Error = "error";

        /// <summary>Server to client heartbeat, every 15 seconds. No payload.</summary>
        public const string Ping = "ping";

        /// <summary>Client to server heartbeat reply. No payload.</summary>
        public const string Pong = "pong";
    }

    /// <summary>Payload of <see cref="MessageTypes.State"/> and <see cref="MessageTypes.Snapshot"/>.</summary>
    /// <typeparam name="TState">The seat's view of the game state.</typeparam>
    public class StateUpdate<TState>
    {
        /// <summary>Gets or sets the state as this seat may see it.</summary>
        public TState State { get; set; } = default!;

        /// <summary>Gets or sets the client seq of the action that produced this state, when it was this client's.</summary>
        public long? AppliedSeq { get; set; }
    }

    /// <summary>Payload of <see cref="MessageTypes.Resync"/>.</summary>
    public class ResyncRequest
    {
        /// <summary>Gets or sets the last state version the client applied.</summary>
        public long LastStateVersion { get; set; }
    }

    /// <summary>Payload of <see cref="MessageTypes.Error"/>.</summary>
    public class ErrorPayload
    {
        /// <summary>Gets or sets a stable snake_case code, e.g. "not_your_turn" or "malformed_envelope".</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Gets or sets a human-readable message.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Gets or sets the seq of the client message that caused the error, if any.</summary>
        public long? Seq { get; set; }
    }
}
