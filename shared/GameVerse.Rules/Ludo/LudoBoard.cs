using System;
using System.Collections.Generic;

namespace GameVerse.Rules.Ludo
{
    /// <summary>
    /// Board geometry (ADR-0008). A token's position is relative to its own colour's path; the shared 52-square track is
    /// numbered absolutely from colour 0's start square. Colour c starts at absolute square c * 13.
    /// </summary>
    public static class LudoBoard
    {
        /// <summary>Tokens per seat.</summary>
        public const int TokensPerSeat = 4;

        /// <summary>Position of a token still in base.</summary>
        public const int Base = -1;

        /// <summary>Squares on the shared track.</summary>
        public const int TrackLength = 52;

        /// <summary>Last relative position on the shared track (the square before the home column).</summary>
        public const int LastTrackPosition = 50;

        /// <summary>Position of a token that has reached home.</summary>
        public const int Home = 56;

        /// <summary>Absolute track squares that are safe when <see cref="LudoConfig.SafeSquares"/> is on: 4 starts, 4 stars.</summary>
        public static IReadOnlyList<int> SafeAbsoluteSquares { get; } = Array.AsReadOnly(new[] { 0, 8, 13, 21, 26, 34, 39, 47 });

        /// <summary>Board colours per seat count: two players sit at opposite corners.</summary>
        /// <param name="playerCount">2 to 4.</param>
        /// <returns>Colour of each seat.</returns>
        public static int[] ColorsFor(int playerCount) => playerCount switch
        {
            2 => new[] { 0, 2 },
            3 => new[] { 0, 1, 2 },
            _ => new[] { 0, 1, 2, 3 },
        };

        /// <summary>The absolute track square of a relative position, or -1 when the token is off the shared track.</summary>
        /// <param name="color">Token colour.</param>
        /// <param name="position">Relative position.</param>
        /// <returns>0 to 51, or -1 (base, home column, home).</returns>
        public static int AbsoluteSquare(int color, int position) =>
            position < 0 || position > LastTrackPosition ? -1 : ((color * 13) + position) % TrackLength;

        /// <summary>Whether an absolute square is one of the 8 safe squares.</summary>
        /// <param name="absolute">Absolute square 0 to 51.</param>
        /// <returns>True for a start or star square.</returns>
        public static bool IsSafe(int absolute)
        {
            for (int i = 0; i < SafeAbsoluteSquares.Count; i++)
            {
                if (SafeAbsoluteSquares[i] == absolute)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
