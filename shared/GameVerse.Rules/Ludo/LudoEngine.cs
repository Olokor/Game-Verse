using System;
using System.Collections.Generic;
using GameVerse.Rules.Engine;
using GameVerse.Rules.Random;

namespace GameVerse.Rules.Ludo
{
    /// <summary>
    /// Ludo rules (GAME_BRIEFS.md section 1, ADR-0008), identical on the server (authoritative) and in Unity. Turn flow:
    /// the seat rolls; if any token can move it then moves one; a six, a capture (when the house rule is on) or a token
    /// reaching home gives another roll. There is no "pass" action: a roll with no legal move ends the turn by itself.
    /// </summary>
    public sealed class LudoEngine : IGameEngine<LudoState, LudoAction, LudoConfig>
    {
        private const string ReasonAllHome = "all_tokens_home";
        private static readonly IReadOnlyList<LudoAction> NoActions = Array.Empty<LudoAction>();
        private static readonly IReadOnlyList<LudoAction> RollOnly = new[] { LudoAction.Roll };

        private readonly Func<int?>? scriptedDice;

        /// <summary>Initializes a new instance of the <see cref="LudoEngine"/> class. Dice come from the state's PRNG.</summary>
        public LudoEngine()
        {
        }

        /// <summary>Test-only: dice values come from <paramref name="scriptedDice"/> until it returns null.</summary>
        /// <param name="scriptedDice">Next die value, or null to fall back to the PRNG.</param>
        internal LudoEngine(Func<int?> scriptedDice)
        {
            this.scriptedDice = scriptedDice;
        }

        /// <inheritdoc/>
        public LudoState NewGame(LudoConfig config, IReadOnlyList<SeatInfo> seats, ulong seed)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            int players = seats?.Count ?? throw new ArgumentNullException(nameof(seats));
            if (players < 2 || players > 4)
            {
                throw new ArgumentException("Ludo needs 2 to 4 seats.", nameof(seats));
            }

            if (config.Teams && players != 4)
            {
                throw new ArgumentException("Team Ludo needs exactly 4 seats (partners sit opposite).", nameof(seats));
            }

            var positions = new int[players * LudoBoard.TokensPerSeat];
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = LudoBoard.Base;
            }

            if (config.QuickMode)
            {
                for (int seat = 0; seat < players; seat++)
                {
                    positions[seat * LudoBoard.TokensPerSeat] = 0; // token 0 starts on its start square
                }
            }

            var rng = new Xoshiro256StarStar(seed);
            return new LudoState(config, LudoBoard.ColorsFor(players), positions, 0, LudoPhase.Roll, 0, 0, Array.Empty<int>(), rng.GetState(), 0);
        }

        /// <inheritdoc/>
        public IReadOnlyList<LudoAction> LegalActions(LudoState state, int seat)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (state.Phase == LudoPhase.Over || seat != state.CurrentSeat)
            {
                return NoActions;
            }

            if (state.Phase == LudoPhase.Roll)
            {
                return RollOnly;
            }

            var moves = Candidates(state, seat, state.LastRoll);
            var actions = new LudoAction[moves.Count];
            for (int i = 0; i < moves.Count; i++)
            {
                actions[i] = LudoAction.Move(moves[i].Token);
            }

            return actions;
        }

        /// <inheritdoc/>
        public ApplyResult<LudoState> Apply(LudoState state, int seat, LudoAction action)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (state.Phase == LudoPhase.Over)
            {
                return ApplyResult.Rejected<LudoState>("game_over", "The game has ended.");
            }

            if (seat != state.CurrentSeat)
            {
                return ApplyResult.Rejected<LudoState>("not_your_turn", "Seat " + state.CurrentSeat + " is to act.");
            }

            return action.IsRoll ? ApplyRoll(state, seat) : ApplyMove(state, seat, action.Token);
        }

        /// <inheritdoc/>
        public GameOutcome? Outcome(LudoState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (state.Phase != LudoPhase.Over)
            {
                return null;
            }

            var placements = Placements(state);
            var winners = new List<int> { placements[0] };
            if (state.Config.Teams)
            {
                winners.Add(Partner(placements[0]));
                winners.Sort();
            }

            return new GameOutcome(OutcomeKind.Win, winners, placements, ReasonAllHome);
        }

        /// <summary>
        /// The state as a seat may see it: the dice PRNG is removed (it would reveal every future roll). Everything else
        /// in Ludo is public.
        /// </summary>
        /// <param name="state">Full state.</param>
        /// <param name="seat">Viewing seat (unused: every seat sees the same).</param>
        /// <returns>A copy without <see cref="LudoState.RngState"/>.</returns>
        public LudoState ViewFor(LudoState state, int seat)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            return state.With(hideRng: true);
        }

        /// <summary>The seat whose tokens <paramref name="seat"/> moves: its own, or its partner's once it has finished (teams).</summary>
        /// <param name="state">State.</param>
        /// <param name="seat">Acting seat.</param>
        /// <returns>Owner of the tokens being moved.</returns>
        public static int ControlledSeat(LudoState state, int seat)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            return state.Config.Teams && state.AllHome(seat) ? Partner(seat) : seat;
        }

        /// <summary>The partner of a seat in team Ludo (opposite corner).</summary>
        /// <param name="seat">Seat 0 to 3.</param>
        /// <returns>The partner's seat.</returns>
        public static int Partner(int seat) => (seat + 2) % 4;

        /// <summary>
        /// Moves available to <paramref name="seat"/> with <paramref name="die"/>, in token order, with what each would
        /// capture. Applies the must-capture rule. Used by the engine and the AI.
        /// </summary>
        /// <param name="state">State.</param>
        /// <param name="seat">Acting seat.</param>
        /// <param name="die">Die value 1 to 6.</param>
        /// <returns>The legal moves.</returns>
        internal static List<LudoMove> Candidates(LudoState state, int seat, int die)
        {
            int owner = ControlledSeat(state, seat);
            int color = state.Colors[owner];
            var moves = new List<LudoMove>(LudoBoard.TokensPerSeat);
            for (int t = 0; t < LudoBoard.TokensPerSeat; t++)
            {
                int from = state.TokenAt(owner, t);
                int to;
                if (from == LudoBoard.Home)
                {
                    continue;
                }

                if (from == LudoBoard.Base)
                {
                    if (die != 6 && state.Config.SixToLeaveBase)
                    {
                        continue;
                    }

                    to = 0;
                }
                else
                {
                    to = from + die;
                    if (to > LudoBoard.Home)
                    {
                        continue; // an exact roll is needed to reach home
                    }
                }

                moves.Add(new LudoMove(owner, t, from, to, Captures(state, owner, color, to)));
            }

            if (state.Config.MustCapture && moves.Exists(m => m.Captured.Length > 0))
            {
                moves.RemoveAll(m => m.Captured.Length == 0);
            }

            return moves;
        }

        private static int[] Captures(LudoState state, int owner, int color, int target)
        {
            int square = LudoBoard.AbsoluteSquare(color, target);
            if (square < 0 || (state.Config.SafeSquares && LudoBoard.IsSafe(square)))
            {
                return Array.Empty<int>();
            }

            var captured = new List<int>();
            for (int other = 0; other < state.PlayerCount; other++)
            {
                if (other == owner || (state.Config.Teams && other == Partner(owner)))
                {
                    continue; // never your own tokens, never your partner's
                }

                for (int t = 0; t < LudoBoard.TokensPerSeat; t++)
                {
                    if (LudoBoard.AbsoluteSquare(state.Colors[other], state.TokenAt(other, t)) == square)
                    {
                        captured.Add((other * LudoBoard.TokensPerSeat) + t);
                    }
                }
            }

            return captured.ToArray();
        }

        private static int EffectivePlacings(LudoState state) =>
            state.Config.Teams ? 1 : System.Math.Min(state.Config.Placings, state.PlayerCount - 1);

        private static bool IsOver(LudoState state, int[] finished)
        {
            if (state.Config.Teams)
            {
                for (int i = 0; i < finished.Length; i++)
                {
                    if (Array.IndexOf(finished, Partner(finished[i])) >= 0)
                    {
                        return true; // both partners of one team are home
                    }
                }

                return false;
            }

            return finished.Length >= EffectivePlacings(state);
        }

        private static int NextSeat(LudoState state, int[] finished, int from)
        {
            for (int k = 1; k <= state.PlayerCount; k++)
            {
                int seat = (from + k) % state.PlayerCount;
                if (state.Config.Teams || Array.IndexOf(finished, seat) < 0)
                {
                    return seat; // finished players drop out, except in teams where they help their partner
                }
            }

            return from;
        }

        private static int Progress(LudoState state, int seat)
        {
            int total = 0;
            for (int t = 0; t < LudoBoard.TokensPerSeat; t++)
            {
                total += state.TokenAt(seat, t) + 1; // base counts 0, home counts 57
            }

            return total;
        }

        private static int TokensHome(LudoState state, int seat)
        {
            int home = 0;
            for (int t = 0; t < LudoBoard.TokensPerSeat; t++)
            {
                if (state.TokenAt(seat, t) == LudoBoard.Home)
                {
                    home++;
                }
            }

            return home;
        }

        private static List<int> Placements(LudoState state)
        {
            var order = new List<int>();
            if (state.Config.Teams)
            {
                // The winning team first (in the order its members finished), then the other team.
                int winner = -1;
                for (int i = 0; i < state.Finished.Count && winner < 0; i++)
                {
                    if (Contains(state.Finished, Partner(state.Finished[i])))
                    {
                        winner = state.Finished[i];
                    }
                }

                int first = IndexOf(state.Finished, winner) <= IndexOf(state.Finished, Partner(winner)) ? winner : Partner(winner);
                order.Add(first);
                order.Add(Partner(first));
            }
            else
            {
                for (int i = 0; i < state.Finished.Count; i++)
                {
                    order.Add(state.Finished[i]);
                }
            }

            var rest = new List<int>();
            for (int seat = 0; seat < state.PlayerCount; seat++)
            {
                if (!order.Contains(seat))
                {
                    rest.Add(seat);
                }
            }

            // Deterministic: tokens home, then total progress, then seat number.
            rest.Sort((a, b) =>
            {
                int byHome = TokensHome(state, b).CompareTo(TokensHome(state, a));
                if (byHome != 0)
                {
                    return byHome;
                }

                int byProgress = Progress(state, b).CompareTo(Progress(state, a));
                return byProgress != 0 ? byProgress : a.CompareTo(b);
            });
            order.AddRange(rest);
            return order;
        }

        private static bool Contains(IReadOnlyList<int> list, int value) => IndexOf(list, value) >= 0;

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

        private ApplyResult<LudoState> ApplyRoll(LudoState state, int seat)
        {
            if (state.Phase != LudoPhase.Roll)
            {
                return ApplyResult.Rejected<LudoState>("must_move", "Move a token with the " + state.LastRoll + " you rolled.");
            }

            ulong[]? nextRng;
            int die;
            int? scripted = scriptedDice?.Invoke();
            if (scripted.HasValue)
            {
                die = scripted.Value;
                nextRng = null; // keep the PRNG where it was
            }
            else if (state.RngState == null)
            {
                return ApplyResult.Rejected<LudoState>("dice_hidden", "This view has no dice: only the authority (server) rolls.");
            }
            else
            {
                var rng = Xoshiro256StarStar.FromState(state.RngState);
                die = rng.NextInt(1, 7);
                nextRng = rng.GetState();
            }

            int sixes = die == 6 ? state.SixesInRow + 1 : 0;
            var finished = new int[state.Finished.Count];
            for (int i = 0; i < finished.Length; i++)
            {
                finished[i] = state.Finished[i];
            }

            if (state.Config.ThreeSixesForfeit && sixes >= 3)
            {
                // Third six in a row: the turn is lost and the six is not moved.
                return ApplyResult.Ok(state.With(currentSeat: NextSeat(state, finished, seat), phase: LudoPhase.Roll, lastRoll: die, sixesInRow: 0, rngState: nextRng));
            }

            if (Candidates(state, seat, die).Count == 0)
            {
                return die == 6
                    ? ApplyResult.Ok(state.With(phase: LudoPhase.Roll, lastRoll: die, sixesInRow: sixes, rngState: nextRng)) // a six still rolls again
                    : ApplyResult.Ok(state.With(currentSeat: NextSeat(state, finished, seat), phase: LudoPhase.Roll, lastRoll: die, sixesInRow: 0, rngState: nextRng));
            }

            return ApplyResult.Ok(state.With(phase: LudoPhase.Move, lastRoll: die, sixesInRow: sixes, rngState: nextRng));
        }

        private static ApplyResult<LudoState> ApplyMove(LudoState state, int seat, int token)
        {
            if (state.Phase != LudoPhase.Move)
            {
                return ApplyResult.Rejected<LudoState>("roll_first", "Roll the die before moving.");
            }

            LudoMove? chosen = null;
            foreach (var move in Candidates(state, seat, state.LastRoll))
            {
                if (move.Token == token)
                {
                    chosen = move;
                }
            }

            if (chosen == null)
            {
                return ApplyResult.Rejected<LudoState>("illegal_move", "Token " + token + " cannot move " + state.LastRoll + ".");
            }

            var positions = state.CopyPositions();
            positions[(chosen.Owner * LudoBoard.TokensPerSeat) + chosen.Token] = chosen.To;
            foreach (int index in chosen.Captured)
            {
                positions[index] = LudoBoard.Base;
            }

            var finishedList = new List<int>(state.Finished);
            var moved = state.With(positions: positions);
            if (moved.AllHome(chosen.Owner) && !finishedList.Contains(chosen.Owner))
            {
                finishedList.Add(chosen.Owner);
            }

            var finished = finishedList.ToArray();
            if (IsOver(state, finished))
            {
                return ApplyResult.Ok(state.With(positions: positions, finished: finished, phase: LudoPhase.Over));
            }

            bool bonus = state.LastRoll == 6 || (chosen.Captured.Length > 0 && state.Config.CaptureBonus) || chosen.To == LudoBoard.Home;
            bool stillPlaying = state.Config.Teams || !finishedList.Contains(seat);
            if (bonus && stillPlaying)
            {
                int sixes = state.LastRoll == 6 ? state.SixesInRow : 0;
                return ApplyResult.Ok(state.With(positions: positions, finished: finished, phase: LudoPhase.Roll, sixesInRow: sixes));
            }

            return ApplyResult.Ok(state.With(positions: positions, finished: finished, currentSeat: NextSeat(state, finished, seat), phase: LudoPhase.Roll, sixesInRow: 0));
        }
    }

    /// <summary>One legal move and its effects (used by the engine and the AI).</summary>
    internal sealed class LudoMove
    {
        public LudoMove(int owner, int token, int from, int to, int[] captured)
        {
            Owner = owner;
            Token = token;
            From = from;
            To = to;
            Captured = captured;
        }

        /// <summary>Gets the seat whose token moves (the partner's, for a finished team player).</summary>
        public int Owner { get; }

        public int Token { get; }

        public int From { get; }

        public int To { get; }

        /// <summary>Gets the flat indices (seat * 4 + token) of opponent tokens sent back to base.</summary>
        public int[] Captured { get; }
    }
}
