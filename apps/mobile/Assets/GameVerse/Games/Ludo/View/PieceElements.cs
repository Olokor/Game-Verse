#nullable enable
using GameVerse.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameVerse.Games.Ludo.View
{
    /// <summary>
    /// One token: a disc in its colour with its colour's shape cut in, and a volt ring when it can be moved. Positioned
    /// absolutely by <see cref="LudoView"/> in cell units, so it follows the board when the screen size changes.
    /// </summary>
    public sealed class TokenElement : VisualElement
    {
        private readonly int color;
        private bool movable;

        public TokenElement(int seat, int token, int color)
        {
            Seat = seat;
            Token = token;
            this.color = color;
            name = "token-" + seat + "-" + token;
            style.position = Position.Absolute;
            generateVisualContent += Draw;
        }

        public int Seat { get; }

        public int Token { get; }

        /// <summary>Gets or sets the token's centre in board cells (fractional while animating).</summary>
        public Vector2 Cell { get; set; }

        /// <summary>Gets or sets an extra scale (squash on landing, pop when captured).</summary>
        public Vector2 Squash { get; set; } = Vector2.one;

        public bool Movable
        {
            get => movable;
            set
            {
                movable = value;
                pickingMode = value ? PickingMode.Position : PickingMode.Ignore;
                MarkDirtyRepaint();
            }
        }

        /// <summary>Places the element for a board whose cells are <paramref name="cellSize"/> pixels.</summary>
        public void Layout(float cellSize, Vector2 offset)
        {
            var size = cellSize * 0.84f;
            style.width = size;
            style.height = size;
            style.left = ((Cell.x + offset.x) * cellSize) - (size / 2);
            style.top = ((Cell.y + offset.y) * cellSize) - (size / 2);
            style.scale = new Scale(new Vector3(Squash.x, Squash.y, 1));
        }

        private void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D;
            var r = contentRect.width / 2;
            var centre = new Vector2(r, r);
            if (movable)
            {
                p.strokeColor = LudoPalette.Highlight;
                p.lineWidth = r * 0.16f;
                p.BeginPath();
                p.Arc(centre, r * 0.92f, 0, 360);
                p.Stroke();
            }

            p.fillColor = LudoPalette.WithAlpha(GameVerseTokens.Color.Bg.Base, 0.55f); // a soft drop under the disc
            p.BeginPath();
            p.Arc(centre + new Vector2(0, r * 0.08f), r * 0.74f, 0, 360);
            p.Fill();

            p.fillColor = LudoPalette.Of(color);
            p.BeginPath();
            p.Arc(centre, r * 0.72f, 0, 360);
            p.Fill();

            p.fillColor = LudoPalette.Ink;
            LudoPalette.FillShape(p, LudoPalette.ShapeOf(color), centre, r * 0.32f);
        }
    }

    /// <summary>A die with pips, drawn in code. Tappable when the local seat may roll.</summary>
    public sealed class DiceElement : VisualElement
    {
        private static readonly Vector2[][] Pips =
        {
            new Vector2[0],
            new[] { new Vector2(0.5f, 0.5f) },
            new[] { new Vector2(0.28f, 0.28f), new Vector2(0.72f, 0.72f) },
            new[] { new Vector2(0.28f, 0.28f), new Vector2(0.5f, 0.5f), new Vector2(0.72f, 0.72f) },
            new[] { new Vector2(0.28f, 0.28f), new Vector2(0.72f, 0.28f), new Vector2(0.28f, 0.72f), new Vector2(0.72f, 0.72f) },
            new[] { new Vector2(0.28f, 0.28f), new Vector2(0.72f, 0.28f), new Vector2(0.5f, 0.5f), new Vector2(0.28f, 0.72f), new Vector2(0.72f, 0.72f) },
            new[] { new Vector2(0.28f, 0.26f), new Vector2(0.72f, 0.26f), new Vector2(0.28f, 0.5f), new Vector2(0.72f, 0.5f), new Vector2(0.28f, 0.74f), new Vector2(0.72f, 0.74f) },
        };

        private int value = 1;
        private bool active;
        private Color accent = Color.white;

        public DiceElement()
        {
            name = "dice";
            generateVisualContent += Draw;
        }

        /// <summary>Gets or sets the face shown (1 to 6; 0 shows a blank face before the first roll).</summary>
        public int Value
        {
            get => value;
            set
            {
                this.value = Mathf.Clamp(value, 0, 6);
                MarkDirtyRepaint();
            }
        }

        /// <summary>Gets or sets a value indicating whether the die is waiting for this device's tap (glows in the seat colour).</summary>
        public bool Active
        {
            get => active;
            set
            {
                active = value;
                pickingMode = value ? PickingMode.Position : PickingMode.Ignore;
                MarkDirtyRepaint();
            }
        }

        public Color Accent
        {
            get => accent;
            set
            {
                accent = value;
                MarkDirtyRepaint();
            }
        }

        private void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D;
            var size = Mathf.Min(contentRect.width, contentRect.height);
            if (active)
            {
                p.strokeColor = accent;
                p.lineWidth = size * 0.06f;
                RoundRect(p, 0.02f * size, 0.02f * size, size * 0.96f, size * 0.22f);
                p.Stroke();
            }

            p.fillColor = GameVerseTokens.Color.Text.Primary;
            RoundRect(p, size * 0.08f, size * 0.08f, size * 0.84f, size * 0.18f);
            p.Fill();

            p.fillColor = GameVerseTokens.Color.Bg.Base;
            foreach (var pip in Pips[value])
            {
                p.BeginPath();
                p.Arc(new Vector2(size * (0.08f + (pip.x * 0.84f)), size * (0.08f + (pip.y * 0.84f))), size * 0.075f, 0, 360);
                p.Fill();
            }
        }

        private static void RoundRect(Painter2D p, float x, float y, float s, float r)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(x + r, y));
            p.ArcTo(new Vector2(x + s, y), new Vector2(x + s, y + s), r);
            p.ArcTo(new Vector2(x + s, y + s), new Vector2(x, y + s), r);
            p.ArcTo(new Vector2(x, y + s), new Vector2(x, y), r);
            p.ArcTo(new Vector2(x, y), new Vector2(x + s, y), r);
            p.ClosePath();
        }
    }
}
