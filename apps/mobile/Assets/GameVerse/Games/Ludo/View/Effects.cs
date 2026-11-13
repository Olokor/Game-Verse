#nullable enable
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameVerse.UI;
using GameVerse.UI.Motion;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameVerse.Games.Ludo.View
{
    /// <summary>
    /// The table's moments: a particle burst on a capture, a ripple from the centre when a token gets home, confetti on a
    /// win. Particles are light Painter2D elements from a pool, never more than <see cref="Cap"/> at once, so a busy
    /// moment never costs a mid-range phone its 60 fps. Under reduced motion nothing is spawned.
    /// </summary>
    public sealed class Effects
    {
        /// <summary>The most particles alive at once.</summary>
        public const int Cap = 30;

        private readonly VisualElement layer;
        private readonly Func<Vector2, Vector2> toPixels;
        private readonly Func<float> cellSize;
        private readonly Stack<Particle> pool = new Stack<Particle>();
        private readonly System.Random random = new System.Random(11);

        /// <param name="layer">Where particles are added (over the board, under nothing that takes touches).</param>
        /// <param name="toPixels">Board cells to pixels inside <paramref name="layer"/>.</param>
        /// <param name="cellSize">One cell in pixels.</param>
        public Effects(VisualElement layer, Func<Vector2, Vector2> toPixels, Func<float> cellSize)
        {
            this.layer = layer;
            this.toPixels = toPixels;
            this.cellSize = cellSize;
        }

        /// <summary>Particles alive now.</summary>
        public int Live { get; private set; }

        /// <summary>Ten or so sparks flying out of a capture square in the captor's colour.</summary>
        public void Burst(Vector2 cell, Color color)
        {
            var unit = cellSize();
            for (var i = 0; i < 12; i++)
            {
                var angle = (float)(random.NextDouble() * Mathf.PI * 2);
                var distance = unit * (0.8f + (float)random.NextDouble() * 0.9f);
                var from = toPixels(cell);
                var to = from + (new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance);
                var size = unit * (0.12f + (float)random.NextDouble() * 0.12f);
                Spawn(Particle.Kind.Dot, i % 3 == 0 ? LudoPalette.Light : color, size, GameVerseTokens.Motion.Game.CaptureBurst, (particle, t) =>
                {
                    particle.Place(Vector2.Lerp(from, to, Tween.EaseOut(t)));
                    particle.style.opacity = 1 - t;
                });
            }
        }

        /// <summary>A ring rippling out from the centre medallion in the token's colour.</summary>
        public void Ripple(Vector2 centreCell, Color color)
        {
            var unit = cellSize();
            var at = toPixels(centreCell);
            for (var i = 0; i < 2; i++)
            {
                var delay = i * 0.35f;
                Spawn(Particle.Kind.Ring, color, unit * 0.6f, GameVerseTokens.Motion.Game.HomeRipple + delay, (particle, t) =>
                {
                    var local = Mathf.Clamp01((t * (1 + delay)) - delay);
                    particle.Resize(unit * (0.6f + (2.6f * Tween.EaseOut(local))));
                    particle.Place(at);
                    particle.style.opacity = local <= 0 ? 0 : 1 - local;
                });
            }
        }

        /// <summary>Confetti in the brand volt and the winner's colour, falling over the board.</summary>
        public void Confetti(Color winner)
        {
            var unit = cellSize();
            var width = unit * 15;
            for (var i = 0; i < Cap; i++)
            {
                var x = (float)random.NextDouble() * width;
                var from = toPixels(Vector2.zero) + new Vector2(x, -unit * (1 + ((float)random.NextDouble() * 4)));
                var to = from + new Vector2(((float)random.NextDouble() - 0.5f) * unit * 4, width * (0.8f + ((float)random.NextDouble() * 0.4f)));
                var spin = (float)random.NextDouble() * 720;
                var delay = (float)random.NextDouble() * 0.3f;
                var kind = i % 3 == 2 ? Particle.Kind.Dot : Particle.Kind.Strip;
                var tint = i % 4 == 0 ? LudoPalette.Light : i % 2 == 0 ? LudoPalette.Highlight : winner;
                var size = unit * (kind == Particle.Kind.Dot ? 0.14f : 0.26f + ((float)random.NextDouble() * 0.12f));
                var flips = 2 + ((float)random.NextDouble() * 3);
                Spawn(kind, tint, size, GameVerseTokens.Motion.Celebrate.DurationMax, (particle, t) =>
                {
                    var local = Mathf.Clamp01((t - delay) / (1 - delay));
                    particle.Place(Vector2.Lerp(from, to, Tween.EaseIn(local)));
                    particle.style.rotate = new Rotate(new Angle(spin * local, AngleUnit.Degree));
                    particle.style.scale = new Scale(new Vector3(Mathf.Cos(local * flips * Mathf.PI * 2), 1, 1)); // a paper flip
                    particle.style.opacity = local <= 0 ? 0 : 1 - Mathf.Max(0, (local - 0.7f) / 0.3f);
                });
            }
        }

        private void Spawn(Particle.Kind kind, Color color, float size, float seconds, Action<Particle, float> animate)
        {
            if (Live >= Cap || MotionPreferences.Reduced)
            {
                return;
            }

            var particle = pool.Count > 0 ? pool.Pop() : new Particle();
            particle.Set(kind, color, size);
            particle.style.opacity = 0;
            layer.Add(particle);
            Live++;
            RunAsync(particle, seconds, animate).Forget();
        }

        private async UniTaskVoid RunAsync(Particle particle, float seconds, Action<Particle, float> animate)
        {
            try
            {
                await Tween.Run(seconds, t => t, t => animate(particle, t));
            }
            finally
            {
                particle.RemoveFromHierarchy();
                pool.Push(particle);
                Live--;
            }
        }

        /// <summary>One particle: a dot, a ring or a confetti strip.</summary>
        public sealed class Particle : VisualElement
        {
            private Kind kind;
            private Color color;
            private float size;

            public Particle()
            {
                pickingMode = PickingMode.Ignore;
                style.position = Position.Absolute;
                generateVisualContent += Draw;
            }

            public enum Kind
            {
                Dot,
                Ring,
                Strip,
            }

            public void Set(Kind particleKind, Color particleColor, float particleSize)
            {
                kind = particleKind;
                color = particleColor;
                Resize(particleSize);
                style.rotate = new Rotate(new Angle(0, AngleUnit.Degree));
            }

            public void Resize(float particleSize)
            {
                size = particleSize;
                style.width = size * 2;
                style.height = size * 2;
                MarkDirtyRepaint();
            }

            /// <summary>Centres the particle on a point in the layer's pixels.</summary>
            public void Place(Vector2 centre)
            {
                style.left = centre.x - size;
                style.top = centre.y - size;
            }

            private void Draw(MeshGenerationContext context)
            {
                var p = context.painter2D;
                var centre = new Vector2(size, size);
                switch (kind)
                {
                    case Kind.Dot:
                        Paint.FillCircle(p, centre, size, color);
                        break;
                    case Kind.Ring:
                        Paint.StrokeArc(p, centre, size * 0.92f, 0, 360, color, Mathf.Max(2, size * 0.08f));
                        break;
                    default:
                        Paint.FillRoundRect(p, new Rect(size * 0.4f, 0, size * 0.5f, size * 2), size * 0.1f, color);
                        break;
                }
            }
        }
    }
}
