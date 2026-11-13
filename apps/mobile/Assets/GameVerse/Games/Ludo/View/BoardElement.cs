#nullable enable
using GameVerse.Games.Ludo.Logic;
using GameVerse.Rules.Ludo;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameVerse.Games.Ludo.View
{
    /// <summary>
    /// The board, drawn in code with Painter2D: crisp at any screen size, and original (no asset pack). Square: it keeps its
    /// height equal to its width. Draws the four yards, the 52-square track with each colour's start square, the home
    /// columns, the safe stars (when the rule is on) and the centre triangles.
    /// </summary>
    public sealed class BoardElement : VisualElement
    {
        private int[] colors = { 0, 1, 2, 3 };
        private bool safeSquares = true;

        public BoardElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        /// <summary>Size of one cell in pixels (the board is 15 cells wide).</summary>
        public float CellSize => contentRect.width / LudoBoardLayout.Size;

        /// <summary>Which colours are in play (empty yards are drawn dimmed) and whether stars are drawn.</summary>
        public void Configure(int[] seatColors, bool safe)
        {
            colors = seatColors;
            safeSquares = safe;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D;
            var cell = CellSize;
            if (cell <= 0)
            {
                return;
            }

            Rect(p, 0, 0, LudoBoardLayout.Size, LudoBoardLayout.Size, cell, LudoPalette.Board);

            for (var color = 0; color < 4; color++)
            {
                var inPlay = System.Array.IndexOf(colors, color) >= 0;
                var tint = LudoPalette.WithAlpha(LudoPalette.Of(color), inPlay ? 0.9f : 0.22f);
                var o = LudoBoardLayout.BaseOrigin[color];
                Rect(p, o.x + 0.15f, o.y + 0.15f, 5.7f, 5.7f, cell, tint);
                Rect(p, o.x + 0.9f, o.y + 0.9f, 4.2f, 4.2f, cell, LudoPalette.Yard);
                for (var t = 0; t < LudoBoard.TokensPerSeat; t++)
                {
                    var slot = LudoBoardLayout.TokenCentre(color, LudoBoard.Base, t) * cell;
                    p.fillColor = LudoPalette.WithAlpha(LudoPalette.Of(color), inPlay ? 0.28f : 0.1f);
                    p.BeginPath();
                    p.Arc(slot, cell * 0.55f, 0, 360);
                    p.Fill();
                }
            }

            for (var i = 0; i < LudoBoardLayout.Track.Length; i++)
            {
                var c = LudoBoardLayout.Track[i];
                var startOf = i % 13 == 0 ? i / 13 : -1;
                var fill = startOf >= 0 ? LudoPalette.WithAlpha(LudoPalette.Of(startOf), 0.85f) : LudoPalette.Cell;
                Rect(p, c.x + 0.04f, c.y + 0.04f, 0.92f, 0.92f, cell, fill);
                if (safeSquares && LudoBoard.IsSafe(i) && startOf < 0)
                {
                    Star(p, new Vector2(c.x + 0.5f, c.y + 0.5f) * cell, cell * 0.32f);
                }
            }

            for (var color = 0; color < 4; color++)
            {
                for (var step = LudoBoard.LastTrackPosition + 1; step < LudoBoard.Home; step++)
                {
                    var c = LudoBoardLayout.Cell(color, step);
                    Rect(p, c.x + 0.04f, c.y + 0.04f, 0.92f, 0.92f, cell, LudoPalette.WithAlpha(LudoPalette.Of(color), 0.55f));
                }

                // The centre: one triangle per colour, pointing in from its home column.
                var toward = LudoBoardLayout.HomeDirection(color);
                var side = new Vector2(-toward.y, toward.x);
                var centre = LudoBoardLayout.Centre;
                p.fillColor = LudoPalette.Of(color);
                p.BeginPath();
                p.MoveTo((centre - (toward * 1.5f) + (side * 1.5f)) * cell);
                p.LineTo((centre - (toward * 1.5f) - (side * 1.5f)) * cell);
                p.LineTo(centre * cell);
                p.ClosePath();
                p.Fill();
            }
        }

        private static void Rect(Painter2D p, float x, float y, float w, float h, float cell, Color color)
        {
            p.fillColor = color;
            p.BeginPath();
            p.MoveTo(new Vector2(x, y) * cell);
            p.LineTo(new Vector2(x + w, y) * cell);
            p.LineTo(new Vector2(x + w, y + h) * cell);
            p.LineTo(new Vector2(x, y + h) * cell);
            p.ClosePath();
            p.Fill();
        }

        private static void Star(Painter2D p, Vector2 centre, float radius)
        {
            p.fillColor = LudoPalette.Star;
            p.BeginPath();
            for (var i = 0; i < 10; i++)
            {
                var angle = (Mathf.PI / 5 * i) - (Mathf.PI / 2);
                var r = i % 2 == 0 ? radius : radius * 0.45f;
                var point = centre + (new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r);
                if (i == 0)
                {
                    p.MoveTo(point);
                }
                else
                {
                    p.LineTo(point);
                }
            }

            p.ClosePath();
            p.Fill();
        }
    }
}
