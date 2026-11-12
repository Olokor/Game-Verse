using System.Collections.Generic;

namespace GameVerse.Contracts.Session
{
    /// <summary>
    /// Starts a single-owner session (local pass-and-play, vs AI, solo) for the signed-in player, who takes seat 0. Online
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
        /// validates its own; Ludo's keys are in <c>GameVerse.Contracts.Ludo.LudoHouseRules</c>.
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

    /// <summary>
    /// Dice fairness by commit-reveal (ADR-0008). The commitment (SHA-256 of a 32-byte server secret) is published when the
    /// session is created; the secret and the seed derived from it are revealed only once the session has ended, so anyone
    /// can check the commitment and replay every roll with GameVerse.Rules.
    /// </summary>
    public class SessionFairness
    {
        /// <summary>Gets or sets the session id.</summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Gets or sets the commitment as lowercase hex, or null for games without server randomness.</summary>
        public string? Commitment { get; set; }

        /// <summary>Gets or sets a value indicating whether the secret is revealed (the session has ended).</summary>
        public bool Revealed { get; set; }

        /// <summary>Gets or sets the secret as lowercase hex, once revealed.</summary>
        public string? Secret { get; set; }

        /// <summary>Gets or sets the PRNG seed derived from the secret, as a decimal string (it exceeds JSON's safe integers).</summary>
        public string? Seed { get; set; }
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
