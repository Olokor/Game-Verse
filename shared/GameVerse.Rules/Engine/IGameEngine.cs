using System.Collections.Generic;

namespace GameVerse.Rules.Engine
{
    /// <summary>
    /// Contract every turn-based game implements once in GameVerse.Rules and uses identically on the
    /// server (authoritative) and in Unity (move highlighting, offline play, AI). See CLAUDE.md 4.2.
    /// <para>
    /// States are immutable from the caller's view: <see cref="Apply"/> returns a new state or a
    /// rejection, and never mutates the state passed in.
    /// </para>
    /// </summary>
    /// <typeparam name="TState">Full game state.</typeparam>
    /// <typeparam name="TAction">A player intent, e.g. "move token 2".</typeparam>
    /// <typeparam name="TConfig">Variant and house-rule settings.</typeparam>
    public interface IGameEngine<TState, TAction, TConfig>
    {
        /// <summary>Creates the starting state. All randomness flows from <paramref name="seed"/>.</summary>
        /// <param name="config">Variant and house rules.</param>
        /// <param name="seats">Seated players, in seat order.</param>
        /// <param name="seed">Seed for the game's PRNG.</param>
        /// <returns>The initial state.</returns>
        TState NewGame(TConfig config, IReadOnlyList<SeatInfo> seats, ulong seed);

        /// <summary>All actions <paramref name="seat"/> may take now. Empty when it is not their turn.</summary>
        /// <param name="state">Current state.</param>
        /// <param name="seat">Seat asking.</param>
        /// <returns>Legal actions in a deterministic order.</returns>
        IReadOnlyList<TAction> LegalActions(TState state, int seat);

        /// <summary>Validates and applies an action.</summary>
        /// <param name="state">Current state (not modified).</param>
        /// <param name="seat">Seat acting.</param>
        /// <param name="action">The intent.</param>
        /// <returns>The new state, or a rejection with a reason code.</returns>
        ApplyResult<TState> Apply(TState state, int seat, TAction action);

        /// <summary>The result once the game is over, otherwise null.</summary>
        /// <param name="state">Current state.</param>
        /// <returns>The outcome, or null while the game continues.</returns>
        GameOutcome? Outcome(TState state);

        /// <summary>The state as <paramref name="seat"/> may see it, with other seats' private information removed.</summary>
        /// <param name="state">Full state.</param>
        /// <param name="seat">Viewing seat.</param>
        /// <returns>A redacted copy safe to send to that seat.</returns>
        TState ViewFor(TState state, int seat);
    }
}
