namespace GameVerse.Rules.Ludo
{
    /// <summary>
    /// Ludo house rules (GAME_BRIEFS.md section 1, ADR-0008). Defaults are the owner's "Classic Naija" set
    /// (2026-11-12). Fixed rules that are not toggles: a six gives another roll, an exact roll is needed to reach home,
    /// and reaching home gives a bonus roll.
    /// </summary>
    public sealed class LudoConfig
    {
        /// <summary>Initializes a new instance of the <see cref="LudoConfig"/> class.</summary>
        /// <param name="sixToLeaveBase">A six is needed to bring a token out of base (default on).</param>
        /// <param name="threeSixesForfeit">A third six in a row forfeits the turn (default on).</param>
        /// <param name="captureBonus">Capturing gives a bonus roll (default on).</param>
        /// <param name="mustCapture">When any move captures, only capturing moves are allowed (default off).</param>
        /// <param name="safeSquares">The 4 start squares and 4 star squares protect tokens from capture (default on).</param>
        /// <param name="quickMode">Every player starts with one token already on its start square (default off).</param>
        /// <param name="placings">
        /// How many places are played for: the game ends once this many players (or teams) have finished. 1 = first home
        /// wins. Capped at players minus 1 (default 1).
        /// </param>
        /// <param name="teams">2v2 with partners opposite (4 players only, default off).</param>
        public LudoConfig(
            bool sixToLeaveBase = true,
            bool threeSixesForfeit = true,
            bool captureBonus = true,
            bool mustCapture = false,
            bool safeSquares = true,
            bool quickMode = false,
            int placings = 1,
            bool teams = false)
        {
            SixToLeaveBase = sixToLeaveBase;
            ThreeSixesForfeit = threeSixesForfeit;
            CaptureBonus = captureBonus;
            MustCapture = mustCapture;
            SafeSquares = safeSquares;
            QuickMode = quickMode;
            Placings = placings < 1 ? 1 : placings > 3 ? 3 : placings;
            Teams = teams;
        }

        /// <summary>Gets the owner's default rule set.</summary>
        public static LudoConfig Default { get; } = new LudoConfig();

        /// <summary>Gets a value indicating whether a six is needed to leave base.</summary>
        public bool SixToLeaveBase { get; }

        /// <summary>Gets a value indicating whether a third six in a row forfeits the turn.</summary>
        public bool ThreeSixesForfeit { get; }

        /// <summary>Gets a value indicating whether a capture gives a bonus roll.</summary>
        public bool CaptureBonus { get; }

        /// <summary>Gets a value indicating whether capturing is compulsory when possible.</summary>
        public bool MustCapture { get; }

        /// <summary>Gets a value indicating whether the 8 safe squares are in play.</summary>
        public bool SafeSquares { get; }

        /// <summary>Gets a value indicating whether each player starts with one token out.</summary>
        public bool QuickMode { get; }

        /// <summary>Gets how many places are played for (1 to 3, capped at players minus 1 when the game starts).</summary>
        public int Placings { get; }

        /// <summary>Gets a value indicating whether this is a 2v2 team game.</summary>
        public bool Teams { get; }
    }
}
