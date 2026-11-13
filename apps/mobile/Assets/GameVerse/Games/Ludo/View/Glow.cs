#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace GameVerse.Games.Ludo.View
{
    /// <summary>
    /// Soft light without banding. Painter2D has no blur, and stacked strokes read as stripes, so glows are a small
    /// white texture with a smooth Gaussian falloff, made once at runtime and used as a tinted 9-slice background.
    /// Breathing then animates only <c>style.opacity</c>, which costs no geometry: important at 60 fps on mid-range phones.
    /// </summary>
    public static class Glow
    {
        /// <summary>Texture size in pixels; the glowing edge is <see cref="Slice"/> pixels wide on each side.</summary>
        private const int Size = 64;

        /// <summary>9-slice border: the corners and edges keep their shape, the centre stretches.</summary>
        public const int Slice = 24;

        private static Texture2D? edge;

        /// <summary>A rounded-rectangle halo: brightest on the rectangle's edge, fading out (and quickly in).</summary>
        public static Texture2D Edge => edge != null ? edge : edge = MakeEdge();

        /// <summary>Applies the halo as a tinted 9-slice background to <paramref name="element"/>.</summary>
        public static void Apply(VisualElement element, Color tint)
        {
            element.style.backgroundImage = new StyleBackground(Edge);
            element.style.unityBackgroundImageTintColor = tint;
            element.style.unitySliceLeft = Slice;
            element.style.unitySliceRight = Slice;
            element.style.unitySliceTop = Slice;
            element.style.unitySliceBottom = Slice;
        }

        private static Texture2D MakeEdge()
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "gv-glow-edge",
            };
            const float inset = 16;
            const float radius = 10;
            var pixels = new Color32[Size * Size];
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    // Signed distance to a rounded rectangle inset from the texture edge (negative inside).
                    var px = Mathf.Abs(x + 0.5f - (Size / 2f)) - ((Size / 2f) - inset - radius);
                    var py = Mathf.Abs(y + 0.5f - (Size / 2f)) - ((Size / 2f) - inset - radius);
                    var outside = new Vector2(Mathf.Max(px, 0), Mathf.Max(py, 0)).magnitude;
                    var distance = outside + Mathf.Min(Mathf.Max(px, py), 0) - radius;
                    var falloff = distance >= 0 ? 6.5f : 3f; // a long soft fade outward, a short one inward
                    var alpha = Mathf.Exp(-(distance / falloff) * (distance / falloff));
                    pixels[(y * Size) + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
