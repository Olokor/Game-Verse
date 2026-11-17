#nullable enable
using System;
using System.Collections.Generic;
using GameVerse.Contracts.Ludo;
using GameVerse.Rules.Ludo;

namespace GameVerse.Games.Ludo.Logic
{
    /// <summary>
    /// Between the wire form (<see cref="LudoStateDto"/>, a seat's view) and the shared engine's <see cref="LudoState"/>.
    /// The client rebuilds a state without the dice PRNG so the engine itself can say which tokens may move: no rule is
    /// written twice. The reverse direction feeds pass-and-play, where the device runs the engine.
    /// </summary>
    public static class LudoStateMapper
    {
        public static LudoConfig Config(LudoRulesDto rules) => new LudoConfig(
            sixToLeaveBase: rules.SixToLeaveBase,
            threeSixesForfeit: rules.ThreeSixesForfeit,
            captureBonus: rules.CaptureBonus,
            mustCapture: rules.MustCapture,
            safeSquares: rules.SafeSquares,
            quickMode: rules.QuickMode,
            placings: rules.Placings,
            teams: rules.Teams);

        /// <summary>The engine state for a seat's view (no PRNG: it can be asked for legal moves, never to roll).</summary>
        public static LudoState ToState(LudoStateDto dto)
        {
            var positions = new List<int>(dto.Tokens.Length * LudoBoard.TokensPerSeat);
            foreach (var seat in dto.Tokens)
            {
                positions.AddRange(seat);
            }

            return new LudoState(
                Config(dto.Rules),
                dto.Colors,
                positions,
                dto.CurrentSeat,
                Phase(dto.Phase),
                dto.LastRoll,
                dto.SixesInRow,
                dto.Finished,
                null,
                dto.MoveNumber,
                dto.Left);
        }

        /// <summary>The wire form of an engine state (pass-and-play feeds the module exactly what a server would).</summary>
        public static LudoStateDto ToDto(LudoState state)
        {
            var c = state.Config;
            var tokens = new int[state.PlayerCount][];
            for (var seat = 0; seat < tokens.Length; seat++)
            {
                tokens[seat] = new int[LudoBoard.TokensPerSeat];
                for (var t = 0; t < LudoBoard.TokensPerSeat; t++)
                {
                    tokens[seat][t] = state.TokenAt(seat, t);
                }
            }

            return new LudoStateDto
            {
                Rules = new LudoRulesDto
                {
                    SixToLeaveBase = c.SixToLeaveBase,
                    ThreeSixesForfeit = c.ThreeSixesForfeit,
                    CaptureBonus = c.CaptureBonus,
                    MustCapture = c.MustCapture,
                    SafeSquares = c.SafeSquares,
                    QuickMode = c.QuickMode,
                    Placings = LudoEngine.EffectivePlacings(c, state.PlayerCount),
                    Teams = c.Teams,
                },
                Colors = ToArray(state.Colors),
                Tokens = tokens,
                CurrentSeat = state.CurrentSeat,
                Phase = state.Phase.ToString().ToLowerInvariant(),
                LastRoll = state.LastRoll,
                SixesInRow = state.SixesInRow,
                Finished = ToArray(state.Finished),
                Left = ToArray(state.Left),
                MoveNumber = state.MoveNumber,
            };
        }

        public static LudoPhase Phase(string phase) => phase switch
        {
            "roll" => LudoPhase.Roll,
            "move" => LudoPhase.Move,
            "over" => LudoPhase.Over,
            _ => throw new ArgumentException("Unknown Ludo phase '" + phase + "'.", nameof(phase)),
        };

        private static int[] ToArray(IReadOnlyList<int> list)
        {
            var array = new int[list.Count];
            for (var i = 0; i < array.Length; i++)
            {
                array[i] = list[i];
            }

            return array;
        }
    }
}
