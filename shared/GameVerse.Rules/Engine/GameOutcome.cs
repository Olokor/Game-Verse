using System;
using System.Collections.Generic;

namespace GameVerse.Rules.Engine
{
    /// <summary>How a game ended.</summary>
    public enum OutcomeKind
    {
        /// <summary>One or more seats won (several for team games).</summary>
        Win = 0,

        /// <summary>No winner.</summary>
        Draw = 1,

        /// <summary>Ended without a sporting result, e.g. every player left.</summary>
        Abandoned = 2,
    }

    /// <summary>Final result of a game, produced by the engine and recorded by the platform.</summary>
    public sealed class GameOutcome
    {
        private static readonly IReadOnlyList<int> NoSeats = Array.Empty<int>();

        /// <summary>Initializes a new instance of the <see cref="GameOutcome"/> class.</summary>
        /// <param name="kind">How the game ended.</param>
        /// <param name="winningSeats">Winning seats; empty for draws and abandoned games.</param>
        /// <param name="placements">Seats in finishing order (1st first), or empty when the game has no placings.</param>
        /// <param name="reason">Stable snake_case reason, e.g. "all_tokens_home", "checkmate", "resign".</param>
        public GameOutcome(OutcomeKind kind, IReadOnlyList<int>? winningSeats, IReadOnlyList<int>? placements, string reason)
        {
            Kind = kind;
            WinningSeats = winningSeats ?? NoSeats;
            Placements = placements ?? NoSeats;
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));

            if (kind == OutcomeKind.Win && WinningSeats.Count == 0)
            {
                throw new ArgumentException("A win needs at least one winning seat.", nameof(winningSeats));
            }

            if (kind != OutcomeKind.Win && WinningSeats.Count != 0)
            {
                throw new ArgumentException("Only a win has winning seats.", nameof(winningSeats));
            }
        }

        /// <summary>Gets how the game ended.</summary>
        public OutcomeKind Kind { get; }

        /// <summary>Gets the winning seats.</summary>
        public IReadOnlyList<int> WinningSeats { get; }

        /// <summary>Gets seats in finishing order, or empty.</summary>
        public IReadOnlyList<int> Placements { get; }

        /// <summary>Gets the stable reason code.</summary>
        public string Reason { get; }

        /// <summary>Single-winner shortcut.</summary>
        /// <param name="seat">Winning seat.</param>
        /// <param name="reason">Reason code.</param>
        /// <returns>The outcome.</returns>
        public static GameOutcome Winner(int seat, string reason) => new GameOutcome(OutcomeKind.Win, new[] { seat }, null, reason);

        /// <summary>Draw shortcut.</summary>
        /// <param name="reason">Reason code.</param>
        /// <returns>The outcome.</returns>
        public static GameOutcome Drawn(string reason) => new GameOutcome(OutcomeKind.Draw, null, null, reason);
    }
}
