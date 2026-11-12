namespace GameVerse.Contracts.Matchmaking
{
    /// <summary>Joins a public queue (CLAUDE.md 4.6): a casual or ranked mode of a game, with a player count it allows.</summary>
    public class JoinQueueRequest
    {
        /// <summary>Gets or sets the catalog game id, e.g. "ludo".</summary>
        public string GameId { get; set; } = string.Empty;

        /// <summary>Gets or sets the catalog mode key, e.g. "quick-match" (casual) or "ranked".</summary>
        public string ModeKey { get; set; } = string.Empty;

        /// <summary>Gets or sets the number of players in the game to find, e.g. 2 or 4.</summary>
        public int PlayerCount { get; set; }
    }

    /// <summary>Where the caller is in matchmaking. Clients poll it while searching.</summary>
    public class QueueStatusDto
    {
        /// <summary>Gets or sets one of <see cref="QueueStatuses"/>.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Gets or sets the game.</summary>
        public string GameId { get; set; } = string.Empty;

        /// <summary>Gets or sets the mode key.</summary>
        public string ModeKey { get; set; } = string.Empty;

        /// <summary>Gets or sets the number of players being matched.</summary>
        public int PlayerCount { get; set; }

        /// <summary>Gets or sets how long the caller has been searching, in whole seconds.</summary>
        public int WaitedSeconds { get; set; }

        /// <summary>
        /// Gets or sets how far from the caller's rating an opponent may be right now (ranked; it widens with the wait), or
        /// null when any rating is accepted (casual, or ranked after a long wait).
        /// </summary>
        public int? RatingBand { get; set; }

        /// <summary>Gets or sets the session to load once matched (GET /sessions/{id}/manifest), else null.</summary>
        public string? SessionId { get; set; }
    }

    /// <summary>Values of <see cref="QueueStatusDto.Status"/>.</summary>
    public static class QueueStatuses
    {
        /// <summary>Still looking for players.</summary>
        public const string Searching = "searching";

        /// <summary>A game was found: load <see cref="QueueStatusDto.SessionId"/>.</summary>
        public const string Matched = "matched";
    }

    /// <summary>A player's Glicko-2 rating in one game.</summary>
    public class RatingDto
    {
        /// <summary>Gets or sets the game.</summary>
        public string GameId { get; set; } = string.Empty;

        /// <summary>Gets or sets the rating, rounded (new players start at 1500).</summary>
        public int Rating { get; set; }

        /// <summary>Gets or sets the rating deviation, rounded: how unsure the rating still is (350 for a new player).</summary>
        public int Deviation { get; set; }

        /// <summary>Gets or sets the number of rated games played.</summary>
        public int GamesPlayed { get; set; }

        /// <summary>Gets or sets a value indicating whether the rating is still settling (deviation above 110).</summary>
        public bool Provisional { get; set; }
    }
}
