using System;

namespace GameVerse.Rules.Engine
{
    /// <summary>A seated player as the rules see them. Platform data (names, cosmetics) stays out of rules.</summary>
    public sealed class SeatInfo
    {
        /// <summary>Initializes a new instance of the <see cref="SeatInfo"/> class.</summary>
        /// <param name="seat">Zero-based seat index.</param>
        /// <param name="playerId">Opaque player id.</param>
        /// <param name="isAI">Whether an AI controls this seat.</param>
        public SeatInfo(int seat, string playerId, bool isAI)
        {
            if (seat < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seat), "Seat must be zero or greater.");
            }

            Seat = seat;
            PlayerId = playerId ?? throw new ArgumentNullException(nameof(playerId));
            IsAI = isAI;
        }

        /// <summary>Gets the zero-based seat index.</summary>
        public int Seat { get; }

        /// <summary>Gets the opaque player id.</summary>
        public string PlayerId { get; }

        /// <summary>Gets a value indicating whether an AI controls this seat.</summary>
        public bool IsAI { get; }
    }
}
