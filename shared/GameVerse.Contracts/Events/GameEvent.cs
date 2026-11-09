using System.Collections.Generic;

namespace GameVerse.Contracts.Events
{
    /// <summary>
    /// Header of an event a game emits to the platform (CLAUDE.md 4.2). In Unity these go through IGameHost;
    /// on web they cross the Unity/React bridge as this JSON.
    /// </summary>
    public class GameEventHeader
    {
        /// <summary>Gets or sets the event type, one of <see cref="GameEventTypes"/>.</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>Gets or sets the session id.</summary>
        public string SessionId { get; set; } = string.Empty;
    }

    /// <summary>A game event with a typed payload.</summary>
    /// <typeparam name="TPayload">Payload type for this event type.</typeparam>
    public class GameEvent<TPayload> : GameEventHeader
    {
        /// <summary>Gets or sets the payload.</summary>
        public TPayload Payload { get; set; } = default!;
    }

    /// <summary>Values of <see cref="GameEventHeader.Type"/>.</summary>
    public static class GameEventTypes
    {
        /// <summary>The game has loaded and can receive state. Payload <see cref="ReadyEvent"/>.</summary>
        public const string Ready = "ready";

        /// <summary>The local player submitted an action. Payload <see cref="ActionSubmittedEvent{TAction}"/>.</summary>
        public const string ActionSubmitted = "action_submitted";

        /// <summary>The game finished presenting a state. Payload <see cref="StateAppliedEvent"/>.</summary>
        public const string StateApplied = "state_applied";

        /// <summary>The match is over. Payload <see cref="MatchEndedEvent"/>.</summary>
        public const string MatchEnded = "match_ended";

        /// <summary>A player left. Payload <see cref="PlayerLeftEvent"/>.</summary>
        public const string PlayerLeft = "player_left";

        /// <summary>The game hit an error. Payload <see cref="GameErrorEvent"/>.</summary>
        public const string Error = "error";

        /// <summary>Analytics. Payload <see cref="TelemetryEvent"/>.</summary>
        public const string Telemetry = "telemetry";
    }

    /// <summary>Payload of <see cref="GameEventTypes.Ready"/>.</summary>
    public class ReadyEvent
    {
        /// <summary>Gets or sets the game build version that loaded.</summary>
        public string GameVersion { get; set; } = string.Empty;
    }

    /// <summary>Payload of <see cref="GameEventTypes.ActionSubmitted"/>.</summary>
    /// <typeparam name="TAction">The game's action type.</typeparam>
    public class ActionSubmittedEvent<TAction>
    {
        /// <summary>Gets or sets the client sequence number.</summary>
        public long Seq { get; set; }

        /// <summary>Gets or sets the action.</summary>
        public TAction Action { get; set; } = default!;
    }

    /// <summary>Payload of <see cref="GameEventTypes.StateApplied"/>.</summary>
    public class StateAppliedEvent
    {
        /// <summary>Gets or sets the state version now on screen.</summary>
        public long StateVersion { get; set; }
    }

    /// <summary>Payload of <see cref="GameEventTypes.MatchEnded"/>. Mirrors the engine's GameOutcome.</summary>
    public class MatchEndedEvent
    {
        /// <summary>Gets or sets "win", "draw" or "abandoned".</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>Gets or sets the winning seats.</summary>
        public List<int> WinningSeats { get; set; } = new List<int>();

        /// <summary>Gets or sets the seats in finishing order, if the game has placings.</summary>
        public List<int> Placements { get; set; } = new List<int>();

        /// <summary>Gets or sets the stable reason code.</summary>
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>Payload of <see cref="GameEventTypes.PlayerLeft"/>.</summary>
    public class PlayerLeftEvent
    {
        /// <summary>Gets or sets the player id.</summary>
        public string PlayerId { get; set; } = string.Empty;

        /// <summary>Gets or sets the seat.</summary>
        public int Seat { get; set; }

        /// <summary>Gets or sets the reason, e.g. "quit", "disconnect_timeout".</summary>
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>Payload of <see cref="GameEventTypes.Error"/>.</summary>
    public class GameErrorEvent
    {
        /// <summary>Gets or sets a stable snake_case code.</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Gets or sets a human-readable message.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether the game cannot continue.</summary>
        public bool Fatal { get; set; }
    }

    /// <summary>Payload of <see cref="GameEventTypes.Telemetry"/>.</summary>
    public class TelemetryEvent
    {
        /// <summary>Gets or sets the event name, e.g. "tutorial_skipped".</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets string properties. Never put personal data here.</summary>
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
    }
}
