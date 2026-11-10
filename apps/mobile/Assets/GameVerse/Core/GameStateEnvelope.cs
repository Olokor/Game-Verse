namespace GameVerse.Core
{
    /// <summary>
    /// An authoritative state handed to <see cref="IGameModule.ApplyServerState"/>. The platform has already dropped stale
    /// versions (CLAUDE.md 4.5). The payload stays as JSON so Core needs no serializer; the game deserializes it into its
    /// own state type with <c>GameVerse.Net.GameVerseJson</c> (the server's realtime envelope, <c>Envelope&lt;StateUpdate&lt;T&gt;&gt;</c>).
    /// </summary>
    public sealed class GameStateEnvelope
    {
        public GameStateEnvelope(string kind, long stateVersion, long? appliedSeq, string stateJson)
        {
            Kind = kind;
            StateVersion = stateVersion;
            AppliedSeq = appliedSeq;
            StateJson = stateJson;
        }

        /// <summary>"state" (incremental) or "snapshot" (full, after reconnect). Values from Contracts.Realtime.MessageTypes.</summary>
        public string Kind { get; }

        /// <summary>Monotonically increasing server version of the match state.</summary>
        public long StateVersion { get; }

        /// <summary>The client action sequence this state applied, if any; lets the game clear its pending input.</summary>
        public long? AppliedSeq { get; }

        /// <summary>The game state (or this seat's view of it) as JSON.</summary>
        public string StateJson { get; }
    }
}
