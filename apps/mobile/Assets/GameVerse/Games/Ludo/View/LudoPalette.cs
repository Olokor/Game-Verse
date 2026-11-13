#nullable enable
using GameVerse.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameVerse.Games.Ludo.View
{
    /// <summary>The shape every colour's tokens carry, so colour is never the only signal (DESIGN_SYSTEM section 10).</summary>
    public enum TokenShape
    {
        Circle,
        Square,
        Triangle,
        Diamond,
    }

    /// <summary>
    /// Board colours, all from design tokens: the four seat colours (color.game.ludo.*), the volt brand accent and the
    /// dark-first neutrals. Light and shade are mixes of those toward text.primary or bg.base, never new hues.
    /// </summary>
    public static class LudoPalette
    {
        public static readonly string[] Names = { "Red", "Green", "Yellow", "Blue" };

        public static Color Of(int color) => color switch
        {
            0 => GameVerseTokens.Color.Game.Ludo.Red,
            1 => GameVerseTokens.Color.Game.Ludo.Green,
            2 => GameVerseTokens.Color.Game.Ludo.Yellow,
            _ => GameVerseTokens.Color.Game.Ludo.Blue,
        };

        public static TokenShape ShapeOf(int color) => (TokenShape)(color & 3);

        public static Color Plate => GameVerseTokens.Color.Bg.Surface;

        public static Color Tile => GameVerseTokens.Color.Bg.Elevated;

        public static Color Well => GameVerseTokens.Color.Bg.Inset;

        public static Color Star => GameVerseTokens.Color.Text.Secondary;

        public static Color Ink => GameVerseTokens.Color.Brand.VoltInk;

        public static Color Highlight => GameVerseTokens.Color.Brand.Volt;

        public static Color Light => GameVerseTokens.Color.Text.Primary;

        public static Color Base => GameVerseTokens.Color.Bg.Base;

        public static Color ShadowSoft => GameVerseTokens.Color.Bg.ShadowSoft;

        public static Color ShadowStrong => GameVerseTokens.Color.Bg.ShadowStrong;

        public static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        /// <summary>The colour mixed toward text.primary (light) by <paramref name="amount"/>.</summary>
        public static Color Lighten(Color color, float amount) => WithAlpha(Color.Lerp(color, Light, amount), color.a);

        /// <summary>The colour mixed toward bg.base (shade) by <paramref name="amount"/>.</summary>
        public static Color Darken(Color color, float amount) => WithAlpha(Color.Lerp(color, Base, amount), color.a);

        /// <summary>Draws a token shape centred at <paramref name="centre"/> with the given radius.</summary>
        public static void FillShape(Painter2D painter, TokenShape shape, Vector2 centre, float radius)
        {
            painter.BeginPath();
            switch (shape)
            {
                case TokenShape.Circle:
                    painter.Arc(centre, radius * 0.86f, 0, 360);
                    break;
                case TokenShape.Square:
                    var r = radius * 0.74f;
                    Paint.RoundRectPath(painter, new Rect(centre.x - r, centre.y - r, r * 2, r * 2), r * 0.28f);
                    break;
                case TokenShape.Triangle:
                    painter.MoveTo(centre + new Vector2(0, -radius));
                    painter.LineTo(centre + new Vector2(radius * 0.92f, radius * 0.66f));
                    painter.LineTo(centre + new Vector2(-radius * 0.92f, radius * 0.66f));
                    break;
                default:
                    painter.MoveTo(centre + new Vector2(0, -radius));
                    painter.LineTo(centre + new Vector2(radius * 0.86f, 0));
                    painter.LineTo(centre + new Vector2(0, radius));
                    painter.LineTo(centre + new Vector2(-radius * 0.86f, 0));
                    break;
            }

            painter.ClosePath();
            painter.Fill();
        }
    }

    /// <summary>Painter2D shapes the table uses: rounded rectangles, ellipses, and soft shadows built from stacked layers.</summary>
    public static class Paint
    {
        public static void RoundRectPath(Painter2D p, Rect r, float radius)
        {
            radius = Mathf.Min(radius, r.width / 2, r.height / 2);
            p.BeginPath();
            p.MoveTo(new Vector2(r.xMin + radius, r.yMin));
            p.ArcTo(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), radius);
            p.ArcTo(new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), radius);
            p.ArcTo(new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin), radius);
            p.ArcTo(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), radius);
            p.ClosePath();
        }

        public static void FillRoundRect(Painter2D p, Rect r, float radius, Color color)
        {
            p.fillColor = color;
            RoundRectPath(p, r, radius);
            p.Fill();
        }

        public static void StrokeRoundRect(Painter2D p, Rect r, float radius, Color color, float width)
        {
            p.strokeColor = color;
            p.lineWidth = width;
            RoundRectPath(p, r, radius);
            p.Stroke();
        }

        public static void FillCircle(Painter2D p, Vector2 centre, float radius, Color color)
        {
            p.fillColor = color;
            p.BeginPath();
            p.Arc(centre, radius, 0, 360);
            p.Fill();
        }

        public static void StrokeArc(Painter2D p, Vector2 centre, float radius, float from, float to, Color color, float width)
        {
            p.strokeColor = color;
            p.lineWidth = width;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.Arc(centre, radius, from, to);
            p.Stroke();
        }

        /// <summary>An ellipse from four Bezier quarters (Painter2D only has circular arcs).</summary>
        public static void FillEllipse(Painter2D p, Vector2 c, float rx, float ry, Color color)
        {
            const float k = 0.5523f;
            p.fillColor = color;
            p.BeginPath();
            p.MoveTo(new Vector2(c.x + rx, c.y));
            p.BezierCurveTo(new Vector2(c.x + rx, c.y + (ry * k)), new Vector2(c.x + (rx * k), c.y + ry), new Vector2(c.x, c.y + ry));
            p.BezierCurveTo(new Vector2(c.x - (rx * k), c.y + ry), new Vector2(c.x - rx, c.y + (ry * k)), new Vector2(c.x - rx, c.y));
            p.BezierCurveTo(new Vector2(c.x - rx, c.y - (ry * k)), new Vector2(c.x - (rx * k), c.y - ry), new Vector2(c.x, c.y - ry));
            p.BezierCurveTo(new Vector2(c.x + (rx * k), c.y - ry), new Vector2(c.x + rx, c.y - (ry * k)), new Vector2(c.x + rx, c.y));
            p.ClosePath();
            p.Fill();
        }

        /// <summary>
        /// A soft shadow under a rounded rectangle: stacked, slightly larger layers at falling alpha (Painter2D has no blur).
        /// </summary>
        public static void SoftShadow(Painter2D p, Rect r, float radius, float spread, float drop, Color color)
        {
            const int layers = 6;
            for (var i = layers; i >= 1; i--)
            {
                var grow = spread * i / layers;
                var layer = new Rect(r.x - grow, r.y - grow + drop, r.width + (grow * 2), r.height + (grow * 2));
                FillRoundRect(p, layer, radius + grow, LudoPalette.WithAlpha(color, color.a / layers));
            }
        }
    }
}
