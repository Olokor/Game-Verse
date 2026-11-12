using System;

namespace GameVerse.Rules.Engine
{
    /// <summary>Why an action was rejected. <see cref="Code"/> is stable and machine-readable; fixtures assert on it.</summary>
    public sealed class RuleError
    {
        /// <summary>Initializes a new instance of the <see cref="RuleError"/> class.</summary>
        /// <param name="code">Stable snake_case code, e.g. "not_your_turn".</param>
        /// <param name="message">Human-readable explanation for logs.</param>
        public RuleError(string code, string message)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        /// <summary>Gets the stable machine-readable code.</summary>
        public string Code { get; }

        /// <summary>Gets the human-readable explanation.</summary>
        public string Message { get; }

        /// <inheritdoc />
        public override string ToString() => Code + ": " + Message;
    }
}
