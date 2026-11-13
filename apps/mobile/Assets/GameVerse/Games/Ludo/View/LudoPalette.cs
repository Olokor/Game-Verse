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

    /// <summary>Board colours, all from design tokens (color.game.ludo.*, color.bg.*, color.text.*).</summary>
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

        public static Color Board => GameVerseTokens.Color.Bg.Surface;

        public static Color Cell => GameVerseTokens.Color.Bg.Elevated;

        public static Color Line => GameVerseTokens.Color.Border.Subtle;

        public static Color Yard => GameVerseTokens.Color.Bg.Inset;

        public static Color Star => GameVerseTokens.Color.Text.Secondary;

        public static Color Ink => GameVerseTokens.Color.Brand.VoltInk;

        public static Color Highlight => GameVerseTokens.Color.Brand.Volt;

        public static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        /// <summary>Draws a token shape centred at <paramref name="centre"/> with the given radius.</summary>
        public static void FillShape(Painter2D painter, TokenShape shape, Vector2 centre, float radius)
        {
            painter.BeginPath();
            switch (shape)
            {
                case TokenShape.Circle:
                    painter.Arc(centre, radius, 0, 360);
                    break;
                case TokenShape.Square:
                    var r = radius * 0.82f;
                    painter.MoveTo(centre + new Vector2(-r, -r));
                    painter.LineTo(centre + new Vector2(r, -r));
                    painter.LineTo(centre + new Vector2(r, r));
                    painter.LineTo(centre + new Vector2(-r, r));
                    break;
                case TokenShape.Triangle:
                    painter.MoveTo(centre + new Vector2(0, -radius));
                    painter.LineTo(centre + new Vector2(radius * 0.92f, radius * 0.7f));
                    painter.LineTo(centre + new Vector2(-radius * 0.92f, radius * 0.7f));
                    break;
                default:
                    painter.MoveTo(centre + new Vector2(0, -radius));
                    painter.LineTo(centre + new Vector2(radius, 0));
                    painter.LineTo(centre + new Vector2(0, radius));
                    painter.LineTo(centre + new Vector2(-radius, 0));
                    break;
            }

            painter.ClosePath();
            painter.Fill();
        }
    }
}
