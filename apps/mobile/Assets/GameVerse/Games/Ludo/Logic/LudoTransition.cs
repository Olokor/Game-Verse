#nullable enable
using System.Collections.Generic;
using GameVerse.Contracts.Ludo;
using GameVerse.Rules.Ludo;

namespace GameVerse.Games.Ludo.Logic
{
    /// <summary>A token sent back to its yard.</summary>
    public readonly struct Capture
    {
        public Capture(int seat, int token, int from)
        {
            Seat = seat;
            Token = token;
            From = from;
        }

        public int Seat { get; }

        public int Token { get; }

        /// <summary>Where it was captured (relative position on its own path).</summary>
        public int From { get; }
    }

    /// <summary>
    /// What changed between two consecutive states, so the board can animate it: a roll, then possibly one token's move
    /// and the captures it caused. Worked out by comparing the states; the server does not describe moves.
    /// </summary>
    public sealed class LudoTransition
    {
        private LudoTransition(bool animate, int? rolledBy, int roll, int? moverSeat, int moverToken, int from, int to, IReadOnlyList<Capture> captures, IReadOnlyList<int> newlyLeft)
        {
            Animate = animate;
            RolledBy = rolledBy;
            Roll = roll;
            MoverSeat = moverSeat;
            MoverToken = moverToken;
            From = from;
            To = to;
            Captures = captures;
            NewlyLeft = newlyLeft;
        }

        /// <summary>False when the states are not one step apart (first state, reconnect snapshot): just show the new one.</summary>
        public bool Animate { get; }

        /// <summary>The seat that rolled the die in this step, or null when it was a move.</summary>
        public int? RolledBy { get; }

        /// <summary>The die value rolled (or moved with).</summary>
        public int Roll { get; }

        /// <summary>The seat whose token moved (its owner: in teams a finished partner may move it), or null.</summary>
        public int? MoverSeat { get; }

        public int MoverToken { get; }

        public int From { get; }

        public int To { get; }

        public IReadOnlyList<Capture> Captures { get; }

        /// <summary>Seats that left (forfeited) in this step.</summary>
        public IReadOnlyList<int> NewlyLeft { get; }

        public bool ReachedHome => MoverSeat != null && To == LudoBoard.Home;

        public static LudoTransition Between(LudoStateDto? before, LudoStateDto after)
        {
            if (before is null || after.MoveNumber != before.MoveNumber + 1 || before.Tokens.Length != after.Tokens.Length)
            {
                return new LudoTransition(false, null, after.LastRoll, null, 0, 0, 0, new Capture[0], new int[0]);
            }

            var left = new List<int>();
            foreach (var seat in after.Left)
            {
                if (System.Array.IndexOf(before.Left, seat) < 0)
                {
                    left.Add(seat);
                }
            }

            int? moverSeat = null;
            int moverToken = 0, from = 0, to = 0;
            var captures = new List<Capture>();
            for (var seat = 0; seat < after.Tokens.Length; seat++)
            {
                for (var t = 0; t < LudoBoard.TokensPerSeat; t++)
                {
                    var was = before.Tokens[seat][t];
                    var now = after.Tokens[seat][t];
                    if (now > was)
                    {
                        moverSeat = seat;
                        moverToken = t;
                        from = was;
                        to = now;
                    }
                    else if (now == LudoBoard.Base && was != LudoBoard.Base)
                    {
                        captures.Add(new Capture(seat, t, was));
                    }
                }
            }

            // No token moved and nobody left: the step was a roll (including a roll with no legal move, which passes the turn).
            int? rolledBy = moverSeat is null && left.Count == 0 ? before.CurrentSeat : (int?)null;
            return new LudoTransition(true, rolledBy, after.LastRoll, moverSeat, moverToken, from, to, captures, left);
        }
    }
}
