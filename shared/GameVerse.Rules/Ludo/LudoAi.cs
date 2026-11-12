using System;
using System.Collections.Generic;
using GameVerse.Rules.Random;

namespace GameVerse.Rules.Ludo
{
    /// <summary>
    /// Ludo AI, shared by the server (AI seats in online games) and Unity (offline vs AI). Deterministic: integer
    /// scores only, ties go to the lowest token index, and level 1 draws from a caller-supplied PRNG that is never the
    /// dice PRNG.
    /// <list type="bullet">
    /// <item>Level 1: a random legal move.</item>
    /// <item>Level 2: integer heuristics, strongest first: capture, reach home, leave base, enter the home column, escape
    /// danger, land on a safe square, progress.</item>
    /// <item>Level 3: expectimax over the next roll: each move is scored by the average position after the next
    /// player's best level-2 reply to each of the six dice.</item>
    /// </list>
    /// </summary>
    public static class LudoAi
    {
        /// <summary>Lowest supported level.</summary>
        public const int MinLevel = 1;

        /// <summary>Highest supported level.</summary>
        public const int MaxLevel = 3;

        private const int WinScore = 1_000_000;

        private static readonly LudoEngine Referee = new LudoEngine();

        /// <summary>Chooses an action for <paramref name="seat"/>. Always a member of <c>LegalActions(state, seat)</c>.</summary>
        /// <param name="state">Full state or a seat's view (the AI never reads the dice PRNG).</param>
        /// <param name="seat">Seat to act; must be the current seat.</param>
        /// <param name="level">1 to 3; out-of-range values are clamped.</param>
        /// <param name="rng">Choice PRNG for level 1 (must not be the dice PRNG). Unused by levels 2 and 3.</param>
        /// <returns>The chosen action.</returns>
        public static LudoAction Choose(LudoState state, int seat, int level, Xoshiro256StarStar rng)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            if (state.Phase == LudoPhase.Over || seat != state.CurrentSeat)
            {
                throw new InvalidOperationException("Seat " + seat + " has nothing to do.");
            }

            if (state.Phase == LudoPhase.Roll)
            {
                return LudoAction.Roll;
            }

            var moves = LudoEngine.Candidates(state, seat, state.LastRoll);
            if (moves.Count == 0)
            {
                throw new InvalidOperationException("A Move phase with no legal move is not a reachable state.");
            }

            int clamped = System.Math.Max(MinLevel, System.Math.Min(MaxLevel, level));
            if (clamped == 1)
            {
                return LudoAction.Move(moves[rng.NextInt(0, moves.Count)].Token);
            }

            int best = 0;
            long bestScore = long.MinValue;
            for (int i = 0; i < moves.Count; i++)
            {
                long score = clamped == 2 ? Heuristic(state, seat, moves[i]) : Lookahead(state, seat, moves[i]);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return LudoAction.Move(moves[best].Token);
        }

        /// <summary>Level 2: how good one move looks right now.</summary>
        private static int Heuristic(LudoState state, int seat, LudoMove move)
        {
            int color = state.Colors[move.Owner];
            int score = 0;
            for (int i = 0; i < move.Captured.Length; i++)
            {
                int victim = move.Captured[i];
                score += 1000 + (state.Positions[victim] * 10); // a capture, worth more the further the victim had come
            }

            if (move.To == LudoBoard.Home)
            {
                score += 800;
            }
            else if (move.From <= LudoBoard.LastTrackPosition && move.To > LudoBoard.LastTrackPosition)
            {
                score += 400; // into the home column: out of reach for good
            }

            if (move.From == LudoBoard.Base)
            {
                score += 500;
            }

            int dangerFrom = Threats(state, move.Owner, LudoBoard.AbsoluteSquare(color, move.From));
            int dangerTo = Threats(state, move.Owner, LudoBoard.AbsoluteSquare(color, move.To), move.Captured);
            score += (dangerFrom * 150) - (dangerTo * 200);

            int target = LudoBoard.AbsoluteSquare(color, move.To);
            if (state.Config.SafeSquares && target >= 0 && LudoBoard.IsSafe(target))
            {
                score += 150;
            }

            return score + (move.To - System.Math.Max(move.From, 0)); // progress breaks the remaining ties
        }

        /// <summary>Level 3: the sum (6 times the average) of position values over the next player's six possible rolls.</summary>
        private static long Lookahead(LudoState state, int seat, LudoMove move)
        {
            var after = LudoEngine.ApplyMove(state, seat, move.Token).State;
            if (after.Phase == LudoPhase.Over)
            {
                return (long)Position(after, seat) * 6;
            }

            long total = 0;
            int actor = after.CurrentSeat;
            for (int die = 1; die <= 6; die++)
            {
                bool forfeits = die == 6 && after.Config.ThreeSixesForfeit && after.SixesInRow >= 2;
                var replies = forfeits ? new List<LudoMove>() : LudoEngine.Candidates(after, actor, die);
                if (replies.Count == 0)
                {
                    total += Position(after, seat);
                    continue;
                }

                // The next player (an opponent, or this seat again on a bonus roll) picks its level-2 best reply.
                var rolled = after.With(phase: LudoPhase.Move, lastRoll: die);
                int best = 0;
                int bestScore = int.MinValue;
                for (int i = 0; i < replies.Count; i++)
                {
                    int score = Heuristic(rolled, actor, replies[i]);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = i;
                    }
                }

                total += Position(LudoEngine.ApplyMove(rolled, actor, replies[best].Token).State, seat);
            }

            return total;
        }

        /// <summary>Static value of a position for <paramref name="seat"/> (and its partner in teams): ours minus theirs.</summary>
        private static int Position(LudoState state, int seat)
        {
            var outcome = Referee.Outcome(state);
            if (outcome != null)
            {
                // By final place: any place above last is a placing (placings 2-3) and beats every unfinished position;
                // higher places beat lower ones. Last place is a loss.
                int sides = state.Config.Teams ? 2 : state.PlayerCount;
                int rank = state.Config.Teams ? (Contains(outcome.WinningSeats, seat) ? 0 : 1) : IndexOf(outcome.Placements, seat);
                return rank >= sides - 1 ? -WinScore : WinScore - (rank * (WinScore / 4));
            }

            int opponents = 0;
            for (int other = 0; other < state.PlayerCount; other++)
            {
                if (!OnSide(state, seat, other) && !state.HasLeft(other))
                {
                    opponents++;
                }
            }

            int value = 0;
            for (int other = 0; other < state.PlayerCount; other++)
            {
                if (state.HasLeft(other) && !OnSide(state, seat, other))
                {
                    continue; // a leaver's tokens are inert
                }

                int tokens = 0;
                int color = state.Colors[other];
                for (int t = 0; t < LudoBoard.TokensPerSeat; t++)
                {
                    int p = state.TokenAt(other, t);
                    if (p == LudoBoard.Base)
                    {
                        continue;
                    }

                    tokens += 100 + (p * 10); // out of base, then progress
                    if (p == LudoBoard.Home)
                    {
                        tokens += 300;
                    }
                    else if (p > LudoBoard.LastTrackPosition)
                    {
                        tokens += 100;
                    }
                    else
                    {
                        tokens -= Threats(state, other, LudoBoard.AbsoluteSquare(color, p)) * (40 + (p * 4));
                    }
                }

                value += OnSide(state, seat, other) ? tokens : -tokens / System.Math.Max(1, opponents); // the average opponent
            }

            return value;
        }

        /// <summary>
        /// How many opponent tokens could land on <paramref name="square"/> with their next roll (1 to 6 squares behind on
        /// the shared track, or in base next to their start square). Zero on safe squares and off the track.
        /// </summary>
        private static int Threats(LudoState state, int owner, int square, int[]? ignore = null)
        {
            if (square < 0 || (state.Config.SafeSquares && LudoBoard.IsSafe(square)))
            {
                return 0;
            }

            int threats = 0;
            for (int other = 0; other < state.PlayerCount; other++)
            {
                if (other == owner || (state.Config.Teams && other == LudoEngine.Partner(owner)) || state.HasLeft(other))
                {
                    continue;
                }

                int color = state.Colors[other];
                bool inBase = false;
                for (int t = 0; t < LudoBoard.TokensPerSeat; t++)
                {
                    int index = (other * LudoBoard.TokensPerSeat) + t;
                    if (ignore != null && Array.IndexOf(ignore, index) >= 0)
                    {
                        inBase = true; // about to be captured: back in base, where it threatens its start square
                        continue;
                    }

                    int p = state.TokenAt(other, t);
                    if (p == LudoBoard.Base)
                    {
                        inBase = true;
                        continue;
                    }

                    int from = LudoBoard.AbsoluteSquare(color, p);
                    if (from < 0)
                    {
                        continue;
                    }

                    int distance = (square - from + LudoBoard.TrackLength) % LudoBoard.TrackLength;
                    if (distance >= 1 && distance <= 6 && p + distance <= LudoBoard.LastTrackPosition)
                    {
                        threats++;
                    }
                }

                if (inBase && square == LudoBoard.AbsoluteSquare(color, 0))
                {
                    threats++; // a token coming out of base lands here
                }
            }

            return threats;
        }

        private static bool OnSide(LudoState state, int seat, int other) =>
            other == seat || (state.Config.Teams && other == LudoEngine.Partner(seat));

        private static int IndexOf(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
