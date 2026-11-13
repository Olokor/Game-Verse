#nullable enable
using System.Collections.Generic;
using GameVerse.Contracts.Ludo;
using GameVerse.Rules.Ludo;

namespace GameVerse.Games.Ludo.Logic
{
    /// <summary>
    /// The board's input rules, without any drawing: whether this device may roll now, which tokens it may tap, and one
    /// action in flight at a time. Legal moves come from the shared engine (<see cref="LudoEngine.LegalActions"/>) run on
    /// the seat's view, so the client never decides a rule; the server checks every action again.
    /// </summary>
    public sealed class LudoController
    {
        private static readonly LudoEngine Engine = new LudoEngine();

        private readonly IReadOnlyCollection<int> localSeats;
        private LudoState? state;

        /// <param name="localSeats">Seats played on this device: one online, every human seat in pass-and-play.</param>
        public LudoController(IReadOnlyCollection<int> localSeats) => this.localSeats = localSeats;

        public LudoStateDto? Current { get; private set; }

        /// <summary>An action was sent and its answer has not arrived yet: no more input until it does.</summary>
        public bool InFlight { get; private set; }

        /// <summary>The seat to act, when this device plays it; otherwise null.</summary>
        public int? LocalSeatToAct =>
            Current != null && Current.Phase != "over" && Contains(localSeats, Current.CurrentSeat) ? Current.CurrentSeat : (int?)null;

        public bool CanRoll => !InFlight && LocalSeatToAct != null && Current!.Phase == "roll";

        /// <summary>Applies an authoritative state (any new state also answers the action in flight, even a rejection's snapshot).</summary>
        public LudoTransition Apply(LudoStateDto next)
        {
            var transition = LudoTransition.Between(Current, next);
            Current = next;
            state = LudoStateMapper.ToState(next);
            InFlight = false;
            return transition;
        }

        /// <summary>
        /// The tokens this device may tap now, as (owner seat, token). In teams a finished player moves the partner's tokens,
        /// so the owner can differ from the acting seat.
        /// </summary>
        public IReadOnlyList<(int Seat, int Token)> MovableTokens()
        {
            var result = new List<(int, int)>();
            if (InFlight || LocalSeatToAct is not { } seat || state is null || Current!.Phase != "move")
            {
                return result;
            }

            var owner = LudoEngine.ControlledSeat(state, seat);
            foreach (var action in Engine.LegalActions(state, seat))
            {
                if (!action.IsRoll && !action.IsForfeit)
                {
                    result.Add((owner, action.Token));
                }
            }

            return result;
        }

        /// <summary>The roll action, or null when rolling is not allowed now. Marks it in flight.</summary>
        public Dictionary<string, object>? Roll()
        {
            if (!CanRoll)
            {
                return null;
            }

            InFlight = true;
            return new Dictionary<string, object> { [LudoActions.Roll] = true };
        }

        /// <summary>The move action for a tapped token, or null when that token may not move now. Marks it in flight.</summary>
        public Dictionary<string, object>? Move(int seat, int token)
        {
            foreach (var (s, t) in MovableTokens())
            {
                if (s == seat && t == token)
                {
                    InFlight = true;
                    return new Dictionary<string, object> { [LudoActions.Move] = token };
                }
            }

            return null;
        }

        /// <summary>The resign action (allowed at any time, also out of turn).</summary>
        public Dictionary<string, object> Forfeit()
        {
            InFlight = true;
            return new Dictionary<string, object> { [LudoActions.Forfeit] = true };
        }

        private static bool Contains(IReadOnlyCollection<int> seats, int seat)
        {
            foreach (var s in seats)
            {
                if (s == seat)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
