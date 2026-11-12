namespace GameVerse.Contracts.Ludo
{
    /// <summary>
    /// Ludo house-rule keys for <c>SessionConfig.HouseRules</c> (values are strings: "true"/"false", placings "1".."3").
    /// Keys left out take the Classic Naija defaults (ADR-0008); unknown keys are rejected.
    /// </summary>
    public static class LudoHouseRules
    {
        /// <summary>Only a six brings a token out of base (default true).</summary>
        public const string SixToLeaveBase = "sixToLeaveBase";

        /// <summary>A third six in a row forfeits the turn (default true).</summary>
        public const string ThreeSixesForfeit = "threeSixesForfeit";

        /// <summary>A capture gives another roll (default true).</summary>
        public const string CaptureBonus = "captureBonus";

        /// <summary>A capturing move must be taken when one exists (default false).</summary>
        public const string MustCapture = "mustCapture";

        /// <summary>The 4 start and 4 star squares protect tokens (default true).</summary>
        public const string SafeSquares = "safeSquares";

        /// <summary>Each player starts with one token out (default false).</summary>
        public const string QuickMode = "quickMode";

        /// <summary>How many players finish before the game ends, "1" to "3", capped at players minus 1 (default "1").</summary>
        public const string Placings = "placings";

        /// <summary>2v2, partners opposite (4 players only, default false).</summary>
        public const string Teams = "teams";
    }

    /// <summary>The effective Ludo rules of a session, after defaults.</summary>
    public class LudoRulesDto
    {
        /// <summary>Gets or sets a value indicating whether only a six brings a token out.</summary>
        public bool SixToLeaveBase { get; set; }

        /// <summary>Gets or sets a value indicating whether a third six forfeits the turn.</summary>
        public bool ThreeSixesForfeit { get; set; }

        /// <summary>Gets or sets a value indicating whether a capture gives another roll.</summary>
        public bool CaptureBonus { get; set; }

        /// <summary>Gets or sets a value indicating whether capturing is compulsory.</summary>
        public bool MustCapture { get; set; }

        /// <summary>Gets or sets a value indicating whether safe squares are on.</summary>
        public bool SafeSquares { get; set; }

        /// <summary>Gets or sets a value indicating whether quick mode is on.</summary>
        public bool QuickMode { get; set; }

        /// <summary>Gets or sets how many players finish before the game ends (effective: capped at players minus 1, 1 for teams).</summary>
        public int Placings { get; set; }

        /// <summary>Gets or sets a value indicating whether this is a 2v2 team game.</summary>
        public bool Teams { get; set; }
    }

    /// <summary>
    /// Ludo actions on the wire: <c>{"roll": true}</c>, <c>{"move": token}</c> (0 to 3; a finished team player's index
    /// refers to the partner's token) or <c>{"forfeit": true}</c> (resign, allowed out of turn).
    /// </summary>
    public static class LudoActions
    {
        /// <summary>Property of a roll action.</summary>
        public const string Roll = "roll";

        /// <summary>Property of a move action; the value is the token index.</summary>
        public const string Move = "move";

        /// <summary>Property of a forfeit action.</summary>
        public const string Forfeit = "forfeit";
    }

    /// <summary>
    /// A player's view of a Ludo state (the manifest's <c>initialState</c>, and later realtime state). It never carries the
    /// dice PRNG: only the server rolls.
    /// </summary>
    public class LudoStateDto
    {
        /// <summary>Gets or sets the rules in force.</summary>
        public LudoRulesDto Rules { get; set; } = new LudoRulesDto();

        /// <summary>Gets or sets the board colour of each seat.</summary>
        public int[] Colors { get; set; } = System.Array.Empty<int>();

        /// <summary>Gets or sets the token positions per seat (4 each): -1 base, 0..50 track, 51..55 home column, 56 home.</summary>
        public int[][] Tokens { get; set; } = System.Array.Empty<int[]>();

        /// <summary>Gets or sets the seat to act.</summary>
        public int CurrentSeat { get; set; }

        /// <summary>Gets or sets the phase: "roll", "move" or "over".</summary>
        public string Phase { get; set; } = "roll";

        /// <summary>Gets or sets the last die value (0 before the first roll).</summary>
        public int LastRoll { get; set; }

        /// <summary>Gets or sets the current seat's sixes in a row.</summary>
        public int SixesInRow { get; set; }

        /// <summary>Gets or sets the seats that have finished, in order.</summary>
        public int[] Finished { get; set; } = System.Array.Empty<int>();

        /// <summary>Gets or sets the seats that forfeited, in order (their tokens stay on the board, inert).</summary>
        public int[] Left { get; set; } = System.Array.Empty<int>();

        /// <summary>Gets or sets the number of state changes so far.</summary>
        public int MoveNumber { get; set; }
    }
}
