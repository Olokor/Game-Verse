using System;
using System.Collections.Generic;

namespace GameVerse.Rules.Ludo
{
    /// <summary>What the seat to act must do next.</summary>
    public enum LudoPhase
    {
        /// <summary>Roll the die.</summary>
        Roll = 0,

        /// <summary>Move a token by <see cref="LudoState.LastRoll"/>.</summary>
        Move = 1,

        /// <summary>The game has ended.</summary>
        Over = 2,
    }

    /// <summary>A player intent: roll the die, or move one token by the rolled amount.</summary>
    public sealed class LudoAction : IEquatable<LudoAction>
    {
        private LudoAction(bool isRoll, int token)
        {
            IsRoll = isRoll;
            Token = token;
        }

        /// <summary>Gets the roll action.</summary>
        public static LudoAction Roll { get; } = new LudoAction(true, -1);

        /// <summary>Gets a value indicating whether this is a roll.</summary>
        public bool IsRoll { get; }

        /// <summary>Gets the token index 0 to 3 for a move, or -1 for a roll.</summary>
        public int Token { get; }

        /// <summary>A move of token <paramref name="token"/> (0 to 3) by the rolled amount.</summary>
        /// <param name="token">Token index.</param>
        /// <returns>The move action.</returns>
        public static LudoAction Move(int token) => new LudoAction(false, token);

        /// <inheritdoc/>
        public bool Equals(LudoAction? other) => other != null && other.IsRoll == IsRoll && other.Token == Token;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as LudoAction);

        /// <inheritdoc/>
        public override int GetHashCode() => IsRoll ? -1 : Token;

        /// <inheritdoc/>
        public override string ToString() => IsRoll ? "roll" : "move " + Token;
    }

    /// <summary>
    /// Full Ludo state (ADR-0008). Immutable: the engine returns a new instance for every change. Token positions are
    /// relative to each token's own path: -1 base, 0 to 50 the shared track (0 = its start square), 51 to 55 its home
    /// column, 56 home. The PRNG state is the dice; <see cref="LudoEngine.ViewFor"/> removes it, because a client that
    /// held it could predict every future roll.
    /// </summary>
    public sealed class LudoState
    {
        private readonly int[] positions;
        private readonly int[] colors;
        private readonly int[] finished;
        private readonly ulong[]? rng;

        /// <summary>Initializes a new instance of the <see cref="LudoState"/> class. Arrays are copied.</summary>
        /// <param name="config">House rules.</param>
        /// <param name="colors">Board colour (0 to 3) per seat.</param>
        /// <param name="positions">Token positions, 4 per seat, seat-major.</param>
        /// <param name="currentSeat">Seat to act.</param>
        /// <param name="phase">What that seat must do.</param>
        /// <param name="lastRoll">The die value to move with (Move phase), or the last roll; 0 before the first roll.</param>
        /// <param name="sixesInRow">Consecutive sixes rolled by the current seat this turn.</param>
        /// <param name="finished">Seats in the order they got all four tokens home.</param>
        /// <param name="rngState">Dice PRNG state (4 values), or null in a seat's view.</param>
        /// <param name="moveNumber">Number of accepted actions so far.</param>
        public LudoState(
            LudoConfig config,
            IReadOnlyList<int> colors,
            IReadOnlyList<int> positions,
            int currentSeat,
            LudoPhase phase,
            int lastRoll,
            int sixesInRow,
            IReadOnlyList<int> finished,
            IReadOnlyList<ulong>? rngState,
            int moveNumber)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            this.colors = Copy(colors);
            this.positions = Copy(positions);
            if (this.positions.Length != this.colors.Length * LudoBoard.TokensPerSeat)
            {
                throw new ArgumentException("Expected 4 token positions per seat.", nameof(positions));
            }

            CurrentSeat = currentSeat;
            Phase = phase;
            LastRoll = lastRoll;
            SixesInRow = sixesInRow;
            this.finished = Copy(finished);
            rng = rngState == null ? null : Copy(rngState);
            MoveNumber = moveNumber;
        }

        /// <summary>Gets the house rules.</summary>
        public LudoConfig Config { get; }

        /// <summary>Gets the number of seats.</summary>
        public int PlayerCount => colors.Length;

        /// <summary>Gets the board colour (0 to 3) of each seat.</summary>
        public IReadOnlyList<int> Colors => colors;

        /// <summary>Gets all token positions, 4 per seat, seat-major.</summary>
        public IReadOnlyList<int> Positions => positions;

        /// <summary>Gets the seat to act.</summary>
        public int CurrentSeat { get; }

        /// <summary>Gets what the current seat must do.</summary>
        public LudoPhase Phase { get; }

        /// <summary>Gets the die value to move with, or the last roll.</summary>
        public int LastRoll { get; }

        /// <summary>Gets the consecutive sixes rolled by the current seat this turn.</summary>
        public int SixesInRow { get; }

        /// <summary>Gets the seats in the order they finished.</summary>
        public IReadOnlyList<int> Finished => finished;

        /// <summary>Gets the dice PRNG state, or null when this is a seat's view (the dice are hidden).</summary>
        public IReadOnlyList<ulong>? RngState => rng;

        /// <summary>Gets the number of accepted actions so far.</summary>
        public int MoveNumber { get; }

        /// <summary>Position of one token.</summary>
        /// <param name="seat">Seat.</param>
        /// <param name="token">Token 0 to 3.</param>
        /// <returns>-1 base, 0 to 50 track, 51 to 55 home column, 56 home.</returns>
        public int TokenAt(int seat, int token) => positions[(seat * LudoBoard.TokensPerSeat) + token];

        /// <summary>Gets a value indicating whether every token of <paramref name="seat"/> is home.</summary>
        /// <param name="seat">Seat.</param>
        /// <returns>True when all four are home.</returns>
        public bool AllHome(int seat)
        {
            for (int t = 0; t < LudoBoard.TokensPerSeat; t++)
            {
                if (TokenAt(seat, t) != LudoBoard.Home)
                {
                    return false;
                }
            }

            return true;
        }

        internal int[] CopyPositions() => (int[])positions.Clone();

        internal LudoState With(
            int[]? positions = null,
            int? currentSeat = null,
            LudoPhase? phase = null,
            int? lastRoll = null,
            int? sixesInRow = null,
            int[]? finished = null,
            ulong[]? rngState = null,
            bool hideRng = false) =>
            new LudoState(
                Config,
                colors,
                positions ?? this.positions,
                currentSeat ?? CurrentSeat,
                phase ?? Phase,
                lastRoll ?? LastRoll,
                sixesInRow ?? SixesInRow,
                finished ?? this.finished,
                hideRng ? null : rngState ?? rng,
                MoveNumber + (hideRng ? 0 : 1));

        private static T[] Copy<T>(IReadOnlyList<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var copy = new T[source.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }
    }
}
