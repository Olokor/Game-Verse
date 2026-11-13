#nullable enable
using System;
using GameVerse.Rules.Ludo;
using UnityEngine;

namespace GameVerse.Games.Ludo.Logic
{
    /// <summary>
    /// Where things sit on the classic 15 x 15 Ludo board, in cell units (column, row; row 0 at the top). Pure geometry for
    /// drawing: the rules (what is legal, what is safe) stay in GameVerse.Rules, and this layout is tested against them.
    /// <para>
    /// Colours go clockwise from the top left: 0 top-left, 1 top-right, 2 bottom-right, 3 bottom-left. Track square 0 is
    /// colour 0's start square; colour c starts at absolute square 13c and enters its home column after its relative
    /// square 50 (LudoBoard.AbsoluteSquare).
    /// </para>
    /// </summary>
    public static class LudoBoardLayout
    {
        public const int Size = 15;

        /// <summary>The 52 shared track squares in travel order, as (column, row).</summary>
        public static readonly Vector2Int[] Track = BuildTrack();

        /// <summary>Each colour's base corner: the top-left cell of its 6 x 6 yard.</summary>
        public static readonly Vector2Int[] BaseOrigin = { new Vector2Int(0, 0), new Vector2Int(9, 0), new Vector2Int(9, 9), new Vector2Int(0, 9) };

        /// <summary>The centre of the board (where finished tokens gather), in cell units.</summary>
        public static readonly Vector2 Centre = new Vector2(7.5f, 7.5f);

        /// <summary>The cell of a track or home-column position (0..55) of a colour. Base (-1) and home (56) have no cell.</summary>
        public static Vector2Int Cell(int color, int position)
        {
            if (position >= 0 && position <= LudoBoard.LastTrackPosition)
            {
                return Track[LudoBoard.AbsoluteSquare(color, position)];
            }

            if (position > LudoBoard.LastTrackPosition && position < LudoBoard.Home)
            {
                return HomeColumn(color, position - LudoBoard.LastTrackPosition);
            }

            throw new ArgumentOutOfRangeException(nameof(position), "Base and home are areas, not cells.");
        }

        /// <summary>
        /// Where a token's centre is drawn, in cell units: its cell's centre on the track, its slot in the yard, or its spot in
        /// its colour's triangle in the centre once home.
        /// </summary>
        public static Vector2 TokenCentre(int color, int position, int token)
        {
            if (position == LudoBoard.Base)
            {
                var origin = BaseOrigin[color];
                return new Vector2(origin.x + 1.75f + (token % 2 * 2.5f), origin.y + 1.75f + (token / 2 * 2.5f));
            }

            if (position == LudoBoard.Home)
            {
                // Each colour's triangle points at the centre from its home column's side.
                var toward = HomeDirection(color);
                var side = new Vector2(-toward.y, toward.x);
                return Centre - (toward * 0.95f) + (side * ((token - 1.5f) * 0.38f));
            }

            var cell = Cell(color, position);
            return new Vector2(cell.x + 0.5f, cell.y + 0.5f);
        }

        /// <summary>The cells a token passes through moving from <paramref name="from"/> to <paramref name="to"/>, one per hop.</summary>
        public static Vector2[] Path(int color, int token, int from, int to)
        {
            if (from == LudoBoard.Base)
            {
                return new[] { TokenCentre(color, 0, token) }; // out of the yard: one hop onto the start square
            }

            var hops = new Vector2[Math.Max(0, to - from)];
            for (var i = 0; i < hops.Length; i++)
            {
                hops[i] = TokenCentre(color, from + i + 1, token);
            }

            return hops;
        }

        /// <summary>The unit direction from a colour's home column into the centre (cells, row grows downward).</summary>
        public static Vector2 HomeDirection(int color) => color switch
        {
            0 => new Vector2(1, 0),
            1 => new Vector2(0, 1),
            2 => new Vector2(-1, 0),
            _ => new Vector2(0, -1),
        };

        private static Vector2Int HomeColumn(int color, int step) => color switch
        {
            0 => new Vector2Int(step, 7),
            1 => new Vector2Int(7, step),
            2 => new Vector2Int(Size - 1 - step, 7),
            _ => new Vector2Int(7, Size - 1 - step),
        };

        private static Vector2Int[] BuildTrack()
        {
            var track = new Vector2Int[LudoBoard.TrackLength];
            var i = 0;
            void Add(int col, int row) => track[i++] = new Vector2Int(col, row);

            for (var c = 1; c <= 5; c++) Add(c, 6);       // left arm, top row, heading right (colour 0 starts at (1,6))
            for (var r = 5; r >= 0; r--) Add(6, r);       // top arm, left column, heading up
            Add(7, 0);                                    // top edge middle
            for (var r = 0; r <= 5; r++) Add(8, r);       // top arm, right column, heading down (colour 1 starts at (8,1))
            for (var c = 9; c <= 14; c++) Add(c, 6);      // right arm, top row, heading right
            Add(14, 7);                                   // right edge middle
            for (var c = 14; c >= 9; c--) Add(c, 8);      // right arm, bottom row, heading left (colour 2 starts at (13,8))
            for (var r = 9; r <= 14; r++) Add(8, r);      // bottom arm, right column, heading down
            Add(7, 14);                                   // bottom edge middle
            for (var r = 14; r >= 9; r--) Add(6, r);      // bottom arm, left column, heading up (colour 3 starts at (6,13))
            for (var c = 5; c >= 0; c--) Add(c, 8);       // left arm, bottom row, heading left
            Add(0, 7);                                    // left edge middle
            Add(0, 6);                                    // and round to colour 0's start
            return track;
        }
    }
}
