using System.Collections.Generic;

namespace GameVerse.Contracts.Matchmaking
{
    /// <summary>Creates a private room (CLAUDE.md 4.6). The creator becomes the host on seat 0.</summary>
    public class CreateRoomRequest
    {
        /// <summary>Gets or sets the catalog game id, e.g. "ludo".</summary>
        public string GameId { get; set; } = string.Empty;

        /// <summary>Gets or sets the number of seats; the game's private-room mode must allow it.</summary>
        public int PlayerCount { get; set; }

        /// <summary>Gets or sets the house rules, validated like a session's (e.g. GameVerse.Contracts.Ludo.LudoHouseRules).</summary>
        public Dictionary<string, string> HouseRules { get; set; } = new Dictionary<string, string>();

        /// <summary>Gets or sets 1 (a single game) or 3 (best of three).</summary>
        public int BestOf { get; set; } = 1;
    }

    /// <summary>Marks the caller ready or not.</summary>
    public class RoomReadyRequest
    {
        /// <summary>Gets or sets a value indicating whether the caller is ready.</summary>
        public bool Ready { get; set; }
    }

    /// <summary>The host changes the house rules (lobby only). Everyone's ready flag resets.</summary>
    public class RoomRulesRequest
    {
        /// <summary>Gets or sets the new house rules.</summary>
        public Dictionary<string, string> HouseRules { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// A private room as every member sees it: from the REST endpoints and pushed on <c>/ws/room/{code}</c> as a
    /// <c>room</c> envelope after every change.
    /// </summary>
    public class RoomDto
    {
        /// <summary>Gets or sets the 6-character code (no O/0, I/1/L).</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Gets or sets the link to share, e.g. https://gameverse.app/r/ABC234.</summary>
        public string ShareUrl { get; set; } = string.Empty;

        /// <summary>Gets or sets the catalog game id.</summary>
        public string GameId { get; set; } = string.Empty;

        /// <summary>Gets or sets the number of seats.</summary>
        public int PlayerCount { get; set; }

        /// <summary>Gets or sets the house rules in force.</summary>
        public Dictionary<string, string> HouseRules { get; set; } = new Dictionary<string, string>();

        /// <summary>Gets or sets 1 or 3.</summary>
        public int BestOf { get; set; }

        /// <summary>Gets or sets one of <see cref="RoomStatuses"/>.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Gets or sets the host's seat.</summary>
        public int HostSeat { get; set; }

        /// <summary>Gets or sets the seats, in order, empty ones included.</summary>
        public List<RoomSeatDto> Seats { get; set; } = new List<RoomSeatDto>();

        /// <summary>Gets or sets the session of the game being played, or of the last one played; null before the first.</summary>
        public string? SessionId { get; set; }

        /// <summary>Gets or sets the number of the current or last game within the series (1-based; 0 before the first).</summary>
        public int GameNumber { get; set; }

        /// <summary>Gets or sets the seats that won the series, once it is decided (both partners in teams).</summary>
        public List<int> SeriesWinners { get; set; } = new List<int>();

        /// <summary>Gets or sets when the room closes if nothing happens, ISO 8601 UTC; null while a game is being played.</summary>
        public string? ExpiresAt { get; set; }
    }

    /// <summary>A seat in a room.</summary>
    public class RoomSeatDto
    {
        /// <summary>Gets or sets the zero-based seat.</summary>
        public int Seat { get; set; }

        /// <summary>Gets or sets the player's id, or null for an empty seat.</summary>
        public string? PlayerId { get; set; }

        /// <summary>Gets or sets the player's display name, or null for an empty seat.</summary>
        public string? DisplayName { get; set; }

        /// <summary>Gets or sets a value indicating whether the player is ready.</summary>
        public bool Ready { get; set; }

        /// <summary>Gets or sets the games this seat has won in the current series.</summary>
        public int Wins { get; set; }
    }

    /// <summary>Values of <see cref="RoomDto.Status"/>.</summary>
    public static class RoomStatuses
    {
        /// <summary>Filling seats and getting ready; the host starts the first game.</summary>
        public const string Lobby = "lobby";

        /// <summary>A game is being played.</summary>
        public const string Playing = "playing";

        /// <summary>A best-of-three is undecided; the next game starts when everyone is ready.</summary>
        public const string BetweenGames = "between_games";

        /// <summary>The series is decided; a rematch (a new series) starts when everyone is ready.</summary>
        public const string SeriesOver = "series_over";

        /// <summary>Everyone left, or the room expired.</summary>
        public const string Closed = "closed";
    }
}
