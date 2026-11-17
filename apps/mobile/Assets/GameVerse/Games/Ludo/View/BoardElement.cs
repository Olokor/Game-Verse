#nullable enable
using Cysharp.Threading.Tasks;
using GameVerse.Games.Ludo.Logic;
using GameVerse.Rules.Ludo;
using GameVerse.UI;
using GameVerse.UI.Motion;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameVerse.Games.Ludo.View
{
    /// <summary>
    /// The board, drawn in code with Painter2D as a raised table: a plate with a soft shadow, dark yard wells with a
    /// luminous rim in each seat's colour, tiles with a lit top edge and a shadowed lip, start squares with a direction
    /// chevron, home columns that brighten toward the centre, soft stars (when the rule is on), and a centre medallion
    /// ringed in the brand volt. Static: it repaints only when its size or the rules change. The turn light lives in four
    /// light <see cref="YardLight"/> layers on top, so a turn change never redraws the board.
    /// </summary>
    public sealed class BoardElement : VisualElement
    {
        /// <summary>Space around the 15 x 15 grid, as a share of the board's width, for the plate's shadow.</summary>
        private const float MarginShare = 0.03f;

        private readonly YardLight[] lights = new YardLight[4];
        private int[] colors = { 0, 1, 2, 3 };
        private bool safeSquares = true;

        public BoardElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            for (var color = 0; color < 4; color++)
            {
                lights[color] = new YardLight(color);
                Add(lights[color]);
            }

            RegisterCallback<GeometryChangedEvent>(_ => PlaceLights());
        }

        /// <summary>Size of one cell in pixels.</summary>
        public float CellSize => contentRect.width * (1 - (2 * MarginShare)) / LudoBoardLayout.Size;

        /// <summary>Top-left of the grid inside the board, in pixels.</summary>
        public Vector2 Origin => Vector2.one * (contentRect.width * MarginShare);

        /// <summary>Which colours are in play (empty yards stay dark) and whether stars are drawn.</summary>
        public void Configure(int[] seatColors, bool safe)
        {
            colors = seatColors;
            safeSquares = safe;
            for (var color = 0; color < 4; color++)
            {
                lights[color].InPlay = System.Array.IndexOf(colors, color) >= 0;
            }

            MarkDirtyRepaint();
        }

        /// <summary>Lights the yard of the colour to act and dims the others (null: nobody, e.g. game over).</summary>
        public void SetTurn(int? color)
        {
            for (var c = 0; c < 4; c++)
            {
                lights[c].SetActive(color == c);
            }
        }

        /// <summary>A point in board cells to pixels inside this element.</summary>
        public Vector2 ToPixels(Vector2 cell) => Origin + (cell * CellSize);

        private void PlaceLights()
        {
            var cell = CellSize;
            for (var color = 0; color < 4; color++)
            {
                var o = LudoBoardLayout.BaseOrigin[color];
                var light = lights[color];
                light.style.left = Origin.x + ((o.x - 0.2f) * cell);
                light.style.top = Origin.y + ((o.y - 0.2f) * cell);
                light.style.width = 6.4f * cell;
                light.style.height = 6.4f * cell;
                light.CellSize = cell;
            }
        }

        private void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D;
            var cell = CellSize;
            if (cell <= 0)
            {
                return;
            }

            var origin = Origin;
            Rect Cells(float x, float y, float w, float h) => new Rect(origin.x + (x * cell), origin.y + (y * cell), w * cell, h * cell);

            // The plate: a soft shadow, the surface, and a faint lit rim.
            var plate = Cells(0, 0, LudoBoardLayout.Size, LudoBoardLayout.Size);
            Paint.SoftShadow(p, plate, cell * 0.6f, origin.x * 0.9f, origin.x * 0.45f, LudoPalette.ShadowStrong);
            Paint.FillRoundRect(p, plate, cell * 0.6f, LudoPalette.Plate);
            Paint.StrokeRoundRect(p, plate, cell * 0.6f, LudoPalette.WithAlpha(LudoPalette.Light, 0.07f), 1);

            for (var color = 0; color < 4; color++)
            {
                DrawYard(p, color, System.Array.IndexOf(colors, color) >= 0, Cells, cell);
            }

            for (var i = 0; i < LudoBoardLayout.Track.Length; i++)
            {
                var c = LudoBoardLayout.Track[i];
                var startOf = i % 13 == 0 ? i / 13 : -1;
                var face = startOf >= 0 ? LudoPalette.Darken(LudoPalette.Of(startOf), 0.1f) : LudoPalette.Tile;
                DrawTile(p, Cells(c.x, c.y, 1, 1), cell, face);
                var centre = Cells(c.x, c.y, 1, 1).center;
                if (startOf >= 0)
                {
                    var next = LudoBoardLayout.Track[(i + 1) % LudoBoardLayout.Track.Length] - c;
                    Chevron(p, centre, new Vector2(next.x, next.y).normalized, cell * 0.2f);
                }
                else if (safeSquares && LudoBoard.IsSafe(i))
                {
                    Paint.FillCircle(p, centre, cell * 0.34f, LudoPalette.WithAlpha(LudoPalette.Star, 0.06f));
                    Star(p, centre, cell * 0.24f, LudoPalette.WithAlpha(LudoPalette.Star, 0.7f));
                }
            }

            for (var color = 0; color < 4; color++)
            {
                for (var step = 1; step < LudoBoard.Home - LudoBoard.LastTrackPosition; step++)
                {
                    var c = LudoBoardLayout.Cell(color, LudoBoard.LastTrackPosition + step);
                    DrawTile(p, Cells(c.x, c.y, 1, 1), cell, LudoPalette.WithAlpha(LudoPalette.Of(color), 0.28f + (0.13f * step)));
                }
            }

            DrawCentre(p, origin, cell);
        }

        private static void DrawYard(Painter2D p, int color, bool inPlay, System.Func<float, float, float, float, Rect> cells, float cell)
        {
            var seat = LudoPalette.Of(color);
            var o = LudoBoardLayout.BaseOrigin[color];
            var yard = cells(o.x + 0.2f, o.y + 0.2f, 5.6f, 5.6f);
            Paint.FillRoundRect(p, yard, cell * 0.55f, LudoPalette.Darken(seat, inPlay ? 0.74f : 0.9f));

            Paint.StrokeRoundRect(p, yard, cell * 0.55f, LudoPalette.WithAlpha(seat, inPlay ? 0.6f : 0.18f), Mathf.Max(1.5f, cell * 0.06f));

            var well = cells(o.x + 0.95f, o.y + 0.95f, 4.1f, 4.1f);
            Paint.FillRoundRect(p, well, cell * 0.45f, LudoPalette.Well);
            Paint.StrokeRoundRect(p, well, cell * 0.45f, LudoPalette.WithAlpha(seat, inPlay ? 0.22f : 0.08f), 1.2f);

            for (var t = 0; t < LudoBoard.TokensPerSeat; t++)
            {
                var slot = LudoBoardLayout.TokenCentre(color, LudoBoard.Base, t);
                var centre = cells(slot.x, slot.y, 0, 0).position;
                Paint.FillCircle(p, centre, cell * 0.58f, LudoPalette.WithAlpha(LudoPalette.Base, 0.7f));
                Paint.FillCircle(p, centre, cell * 0.5f, LudoPalette.Darken(seat, inPlay ? 0.66f : 0.85f));
                // Light from above: the recess catches it on its lower inside edge.
                Paint.StrokeArc(p, centre, cell * 0.5f, 25, 155, LudoPalette.WithAlpha(LudoPalette.Light, inPlay ? 0.16f : 0.05f), 1.2f);
            }
        }

        private static void DrawTile(Painter2D p, Rect cellRect, float cell, Color face)
        {
            var tile = new Rect(cellRect.x + (cell * 0.06f), cellRect.y + (cell * 0.06f), cell * 0.88f, cell * 0.88f);
            var radius = cell * 0.18f;
            Paint.FillRoundRect(p, new Rect(tile.x, tile.y + (cell * 0.05f), tile.width, tile.height), radius, LudoPalette.WithAlpha(LudoPalette.Base, 0.55f));
            Paint.FillRoundRect(p, tile, radius, face);
            Paint.FillRoundRect(p, new Rect(tile.x + (cell * 0.1f), tile.y + (cell * 0.04f), tile.width - (cell * 0.2f), cell * 0.05f), cell * 0.025f, LudoPalette.WithAlpha(LudoPalette.Light, 0.07f));
        }

        private static void DrawCentre(Painter2D p, Vector2 origin, float cell)
        {
            var centre = origin + (LudoBoardLayout.Centre * cell);
            for (var color = 0; color < 4; color++)
            {
                var toward = LudoBoardLayout.HomeDirection(color);
                var side = new Vector2(-toward.y, toward.x);
                var a = centre + (((-toward * 1.5f) + (side * 1.5f)) * cell);
                var b = centre + (((-toward * 1.5f) - (side * 1.5f)) * cell);
                p.fillColor = LudoPalette.Darken(LudoPalette.Of(color), 0.08f);
                p.BeginPath();
                p.MoveTo(a);
                p.LineTo(b);
                p.LineTo(centre);
                p.ClosePath();
                p.Fill();

                // A bevel: the outer edge catches light, the inner edges fall into shade.
                p.strokeColor = LudoPalette.WithAlpha(LudoPalette.Lighten(LudoPalette.Of(color), 0.4f), 0.45f);
                p.lineWidth = 1.2f;
                p.BeginPath();
                p.MoveTo(a);
                p.LineTo(b);
                p.Stroke();
            }

            Paint.FillCircle(p, centre, cell * 0.72f, LudoPalette.WithAlpha(LudoPalette.Base, 0.45f));
            Paint.FillCircle(p, centre, cell * 0.6f, LudoPalette.Base);
            Paint.StrokeArc(p, centre, cell * 0.6f, 0, 360, LudoPalette.Highlight, cell * 0.07f);
            Paint.StrokeArc(p, centre, cell * 0.42f, 0, 360, LudoPalette.WithAlpha(LudoPalette.Light, 0.08f), 1);
            Paint.FillCircle(p, centre, cell * 0.2f, LudoPalette.WithAlpha(LudoPalette.Highlight, 0.25f)); // the brand core
            Paint.FillCircle(p, centre, cell * 0.12f, LudoPalette.Highlight);
        }

        private static void Chevron(Painter2D p, Vector2 centre, Vector2 direction, float size)
        {
            var side = new Vector2(-direction.y, direction.x);
            var tip = centre + (direction * size * 0.6f);
            p.strokeColor = LudoPalette.WithAlpha(LudoPalette.Ink, 0.55f);
            p.lineWidth = Mathf.Max(1.5f, size * 0.32f);
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            p.MoveTo(tip - (direction * size) + (side * size));
            p.LineTo(tip);
            p.LineTo(tip - (direction * size) - (side * size));
            p.Stroke();
        }

        private static void Star(Painter2D p, Vector2 centre, float radius, Color color)
        {
            p.fillColor = color;
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

    /// <summary>
    /// The turn light over one yard: a soft halo and a crisp rim in the seat colour while that seat acts, a shade while
    /// another seat does. Built from styled child elements (a 9-slice glow texture, a bordered rim, a dim fill), so
    /// breathing and turn changes animate <c>style.opacity</c> only and never re-tessellate the board.
    /// </summary>
    public sealed class YardLight : VisualElement
    {
        private readonly VisualElement halo = new VisualElement();
        private readonly VisualElement rim = new VisualElement();
        private readonly VisualElement shade = new VisualElement();
        private readonly IVisualElementScheduledItem breathing;
        private float level;
        private bool active;
        private bool inPlay;

        public YardLight(int color)
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            var seat = LudoPalette.Of(color);
            foreach (var layer in new[] { shade, halo, rim })
            {
                layer.pickingMode = PickingMode.Ignore;
                layer.style.position = Position.Absolute;
                Add(layer);
            }

            Glow.Apply(halo, LudoPalette.WithAlpha(seat, 0.85f));
            rim.style.borderTopColor = rim.style.borderRightColor = rim.style.borderBottomColor = rim.style.borderLeftColor = LudoPalette.Lighten(seat, 0.18f);
            shade.style.backgroundColor = LudoPalette.WithAlpha(LudoPalette.Base, 0.42f);
            breathing = schedule.Execute(Breathe).Every(33);
            breathing.Pause();
            Apply();
        }

        public bool InPlay
        {
            get => inPlay;
            set
            {
                inPlay = value;
                style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>Sizes the layers for a board whose cells are <paramref name="value"/> pixels.</summary>
        public float CellSize
        {
            set
            {
                var cell = value;
                Place(shade, 0.4f * cell, 5.6f * cell, 0.55f * cell, 0);
                Place(rim, 0.4f * cell, 5.6f * cell, 0.55f * cell, Mathf.Max(2, cell * 0.09f));
                // The halo extends past the yard; its slice scale puts the texture's brightest line (16px into the 64px
                // texture) exactly on the yard's rim, 0.4 cells in from this element's edge.
                Place(halo, 0, 6.4f * cell, 0, 0);
                halo.style.unitySliceScale = 0.4f * cell / 16f;
            }
        }

        public void SetActive(bool on)
        {
            if (active == on)
            {
                return;
            }

            active = on;
            if (on && !MotionPreferences.Reduced)
            {
                breathing.Resume();
            }
            else
            {
                breathing.Pause();
            }

            var from = level;
            Tween.Run(GameVerseTokens.Motion.Base.Duration, Tween.Standard, t =>
            {
                level = Mathf.Lerp(from, on ? 1 : 0, t);
                Apply();
            }).Forget();
        }

        private void Breathe() => Apply();

        private void Apply()
        {
            var breath = active && !MotionPreferences.Reduced ? 0.72f + (0.28f * Mathf.Sin(Time.realtimeSinceStartup * Mathf.PI * 2 / GameVerseTokens.Motion.Ambient.Duration)) : 1f;
            halo.style.opacity = level * breath;
            rim.style.opacity = level;
            shade.style.opacity = 1 - level;
        }

        private static void Place(VisualElement layer, float offset, float size, float radius, float border)
        {
            layer.style.left = offset;
            layer.style.top = offset;
            layer.style.width = size;
            layer.style.height = size;
            layer.style.borderTopLeftRadius = layer.style.borderTopRightRadius = layer.style.borderBottomLeftRadius = layer.style.borderBottomRightRadius = radius;
            layer.style.borderTopWidth = layer.style.borderRightWidth = layer.style.borderBottomWidth = layer.style.borderLeftWidth = border;
        }
    }
}
