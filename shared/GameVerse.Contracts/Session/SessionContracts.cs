using System.Collections.Generic;

namespace GameVerse.Contracts.Session
{
    /// <summary>
    /// Starts an offline session (local pass-and-play, vs AI, solo) for the signed-in player, who takes seat 0. Online
    /// sessions are created by private rooms and matchmaking instead (Prompts 1.4, 1.5).
    /// </summary>
    public class CreateSessionRequest
    {
        /// <summary>Gets or sets the catalog game id, e.g. "ludo".</summary>
        public string GameId { get; set; } = string.Empty;

        /// <summary>Gets or sets the catalog mode key, e.g. "vs-ai".</summary>
        public string Mode { get; set; } = string.Empty;

        /// <summary>Gets or sets the number of seats (humans and AI), one of the mode's player counts.</summary>
        public int PlayerCount { get; set; }

        /// <summary>Gets or sets the AI level 1 to 3 for vs AI modes (default 1).</summary>
        public int? AiLevel { get; set; }

        /// <summary>Gets or sets the rules variant, when the game has several.</summary>
        public string? Variant { get; set; }

        /// <summary>
        /// Gets or sets house-rule toggles as strings (e.g. "sixToLeaveBase": "true"). Each game's engine parses and
        /// validates its own (Ludo: Prompt 1.3).
        /// </summary>
        public Dictionary<string, string> HouseRules { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>One of the player's sessions.</summary>
    public class SessionSummary
    {
        /// <summary>Gets or sets the session id.</summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Gets or sets the game id.</summary>
        public string GameId { get; set; } = string.Empty;

        /// <summary>Gets or sets the mode key.</summary>
        public string Mode { get; set; } = string.Empty;

        /// <summary>Gets or sets the status, one of <see cref="SessionStatuses"/>.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Gets or sets when the session was created (ISO 8601, UTC).</summary>
        public string CreatedAt { get; set; } = string.Empty;
    }

    /// <summary>Values of <see cref="SessionSummary.Status"/>.</summary>
    public static class SessionStatuses
    {
        /// <summary>Waiting for players (online rooms).</summary>
        public const string Created = "created";

        /// <summary>In play.</summary>
        public const string Active = "active";

        /// <summary>A result was recorded.</summary>
        public const string Finished = "finished";

        /// <summary>Ended without a result.</summary>
        public const string Abandoned = "abandoned";
    }
}
