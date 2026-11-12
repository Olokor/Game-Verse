using System;

namespace GameVerse.Rules.Engine
{
    /// <summary>Result of <see cref="IGameEngine{TState, TAction, TConfig}.Apply"/>: a new state or a rejection.</summary>
    /// <typeparam name="TState">Game state type.</typeparam>
    public sealed class ApplyResult<TState>
    {
        private readonly TState state;

        internal ApplyResult(TState state, RuleError? error)
        {
            this.state = state;
            Error = error;
        }

        /// <summary>Gets a value indicating whether the action was accepted.</summary>
        public bool IsOk => Error == null;

        /// <summary>Gets the rejection reason, or null when accepted.</summary>
        public RuleError? Error { get; }

        /// <summary>Gets the new state. Throws when the action was rejected.</summary>
        public TState State
        {
            get
            {
                if (Error != null)
                {
                    throw new InvalidOperationException("Action was rejected (" + Error + "); there is no new state.");
                }

                return state;
            }
        }
    }

    /// <summary>Factory methods for <see cref="ApplyResult{TState}"/>.</summary>
    public static class ApplyResult
    {
        /// <summary>Accepted action producing <paramref name="state"/>.</summary>
        /// <typeparam name="TState">Game state type.</typeparam>
        /// <param name="state">The new state.</param>
        /// <returns>An accepted result.</returns>
        public static ApplyResult<TState> Ok<TState>(TState state) => new ApplyResult<TState>(state, null);

        /// <summary>Rejected action.</summary>
        /// <typeparam name="TState">Game state type.</typeparam>
        /// <param name="code">Stable snake_case code.</param>
        /// <param name="message">Human-readable explanation.</param>
        /// <returns>A rejected result.</returns>
        public static ApplyResult<TState> Rejected<TState>(string code, string message) =>
            new ApplyResult<TState>(default!, new RuleError(code, message));
    }
}
