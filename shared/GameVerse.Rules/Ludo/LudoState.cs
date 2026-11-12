using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

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

    /// <summary>
    /// A player intent: roll the die, move one token by the rolled amount, or forfeit (resign; the server also sends it
    /// for a seat after a turn timeout or the disconnect grace window, CLAUDE.md 4.5).
    /// </summary>
    public sealed class LudoAction : IEquatable<LudoAction>
    {
        private const int RollToken = -1;
        private const int ForfeitToken = -2;

        private LudoAction(int token)
        {
            Token = token;
        }

        /// <summary>Gets the roll action.</summary>
        public static LudoAction Roll { get; } = new LudoAction(RollToken);

        /// <summary>
        /// Gets the forfeit action. Any seat still playing may send it at any time, even out of turn, so it is not listed
        /// by <see cref="LudoEngine.LegalActions"/>.
        /// </summary>
        public static LudoAction Forfeit { get; } = new LudoAction(ForfeitToken);

        /// <summary>Gets a value indicating whether this is a roll.</summary>
        public bool IsRoll => Token == RollToken;

        /// <summary>Gets a value indicating whether this is a forfeit.</summary>
        public bool IsForfeit => Token == ForfeitToken;

        /// <summary>
        /// Gets the token index 0 to 3 for a move (negative for a roll or forfeit). For a finished team player the index
        /// refers to the partner's token (see <see cref="LudoEngine.ControlledSeat"/>).
        /// </summary>
        public int Token { get; }

        /// <summary>A move of token <paramref name="token"/> (0 to 3) by the rolled amount.</summary>
        /// <param name="token">Token index.</param>
        /// <returns>The move action.</returns>
        public static LudoAction Move(int token) => new LudoAction(token < 0 ? int.MaxValue : token);

        /// <inheritdoc/>
        public bool Equals(LudoAction? other) => other != null && other.Token == Token;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as LudoAction);

        /// <inheritdoc/>
        public override int GetHashCode() => Token;

        /// <inheritdoc/>
        public override string ToString() => IsRoll ? "roll" : IsForfeit ? "forfeit" : "move " + Token;
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
        private readonly int[] left;
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
        /// <param name="left">Seats that forfeited, in order (null for none).</param>
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
            int moveNumber,
            IReadOnlyList<int>? left = null)
        {
            // Validated because states are also restored from snapshots (Redis, fixtures), not only built by the engine.
            Config = config ?? throw new ArgumentNullException(nameof(config));
            this.colors = Copy(colors);
            if (this.colors.Length < 2 || this.colors.Length > 4)
            {
                throw new ArgumentException("Ludo has 2 to 4 seats.", nameof(colors));
            }

            this.positions = Copy(positions);
            if (this.positions.Length != this.colors.Length * LudoBoard.TokensPerSeat)
            {
                throw new ArgumentException("Expected 4 token positions per seat.", nameof(positions));
            }

            for (int i = 0; i < this.positions.Length; i++)
            {
                if (this.positions[i] < LudoBoard.Base || this.positions[i] > LudoBoard.Home)
                {
                    throw new ArgumentException("Token positions are -1 to 56.", nameof(positions));
                }
            }

            if (currentSeat < 0 || currentSeat >= this.colors.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(currentSeat));
            }

            if (lastRoll < 0 || lastRoll > 6 || (phase == LudoPhase.Move && lastRoll == 0))
            {
                throw new ArgumentOutOfRangeException(nameof(lastRoll));
            }

            if (sixesInRow < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sixesInRow));
            }

            this.finished = SeatList(finished, this.colors.Length, nameof(finished));
            this.left = left == null ? Array.Empty<int>() : SeatList(left, this.colors.Length, nameof(left));
            if (rngState != null && rngState.Count != 4)
            {
                throw new ArgumentException("The dice PRNG state is 4 values.", nameof(rngState));
            }

            CurrentSeat = currentSeat;
            Phase = phase;
            LastRoll = lastRoll;
            SixesInRow = sixesInRow;
            rng = rngState == null ? null : Copy(rngState);
            MoveNumber = moveNumber;

            // Read-only wrappers: callers cannot cast them back to arrays and change the state (CLAUDE.md 4.2).
            Colors = Array.AsReadOnly(this.colors);
            Positions = Array.AsReadOnly(this.positions);
            Finished = Array.AsReadOnly(this.finished);
            Left = Array.AsReadOnly(this.left);
            RngState = rng == null ? null : Array.AsReadOnly(rng);
        }

        /// <summary>Gets the house rules.</summary>
        public LudoConfig Config { get; }

        /// <summary>Gets the number of seats.</summary>
        public int PlayerCount => colors.Length;

        /// <summary>Gets the board colour (0 to 3) of each seat.</summary>
        public IReadOnlyList<int> Colors { get; }

        /// <summary>Gets all token positions, 4 per seat, seat-major.</summary>
        public IReadOnlyList<int> Positions { get; }

        /// <summary>Gets the seat to act.</summary>
        public int CurrentSeat { get; }

        /// <summary>Gets what the current seat must do.</summary>
        public LudoPhase Phase { get; }

        /// <summary>Gets the die value to move with, or the last roll.</summary>
        public int LastRoll { get; }

        /// <summary>Gets the consecutive sixes rolled by the current seat this turn.</summary>
        public int SixesInRow { get; }

        /// <summary>Gets the seats in the order they finished.</summary>
        public IReadOnlyList<int> Finished { get; }

        /// <summary>Gets the seats that forfeited, in order. Their tokens stay on the board but are inert.</summary>
        public IReadOnlyList<int> Left { get; }

        /// <summary>Gets the dice PRNG state, or null when this is a seat's view (the dice are hidden).</summary>
        public IReadOnlyList<ulong>? RngState { get; }

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
            bool hideRng = false,
            int[]? left = null) =>
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
                MoveNumber + (hideRng ? 0 : 1),
                left ?? this.left);

        internal bool HasLeft(int seat) => Array.IndexOf(left, seat) >= 0;

        private static int[] SeatList(IReadOnlyList<int> seats, int playerCount, string name)
        {
            var copy = Copy(seats);
            for (int i = 0; i < copy.Length; i++)
            {
                if (copy[i] < 0 || copy[i] >= playerCount || Array.IndexOf(copy, copy[i]) != i)
                {
                    throw new ArgumentException("Seats must be distinct and in range.", name);
                }
            }

            return copy;
        }

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
