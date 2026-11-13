#nullable enable
using Cysharp.Threading.Tasks;
using GameVerse.UI;
using GameVerse.UI.Motion;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameVerse.Games.Ludo.View
{
    /// <summary>
    /// One token, drawn as a piece rather than a sticker: a contact shadow, a darker rim, a body lit from the top left, a
    /// specular glint, and the seat's shape emblem (colour is never the only signal). A token that can move lifts, casts a
    /// longer shadow and wears a breathing volt ring; the local player's other tokens dim slightly while they choose.
    /// The element is at least the 44px touch target even when the piece drawn is smaller.
    /// </summary>
    public sealed class TokenElement : VisualElement
    {
        /// <summary>Minimum touch area in panel pixels (layout.touch-target).</summary>
        public const float TouchTarget = GameVerseTokens.Layout.TouchTarget;

        private readonly int color;
        private readonly Label stack = new Label();
        private readonly VisualElement ring = new VisualElement();
        private readonly IVisualElementScheduledItem breathing;
        private bool movable;
        private bool pressed;
        private float pieceSize;
        private float lastCell;
        private Vector2 lastOrigin;
        private Vector2 lastOffset;
        private bool lastInYard;

        public TokenElement(int seat, int token, int color)
        {
            Seat = seat;
            Token = token;
            this.color = color;
            name = "token-" + seat + "-" + token;
            style.position = Position.Absolute;
            pickingMode = PickingMode.Ignore;
            stack.AddToClassList("ludo__stack");
            stack.pickingMode = PickingMode.Ignore;
            stack.style.display = DisplayStyle.None;
            ring.AddToClassList("ludo__ring");
            ring.pickingMode = PickingMode.Ignore;
            ring.style.display = DisplayStyle.None;
            Add(ring);
            Add(stack);
            generateVisualContent += Draw;
            breathing = schedule.Execute(() =>
                ring.style.opacity = 0.55f + (0.45f * Mathf.Sin(Time.realtimeSinceStartup * Mathf.PI * 2 / GameVerseTokens.Motion.Ambient.Duration))).Every(33);
            breathing.Pause();

            // Pressed: the piece sinks a little under the finger.
            RegisterCallback<PointerDownEvent>(_ => SetPressed(true));
            RegisterCallback<PointerUpEvent>(_ => SetPressed(false));
            RegisterCallback<PointerLeaveEvent>(_ => SetPressed(false));
            RegisterCallback<PointerCancelEvent>(_ => SetPressed(false));
        }

        public int Seat { get; }

        public int Token { get; }

        /// <summary>Gets or sets the token's centre in board cells (fractional while animating).</summary>
        public Vector2 Cell { get; set; }

        /// <summary>Gets or sets an extra scale (squash on landing, pop when captured).</summary>
        public Vector2 Squash { get; set; } = Vector2.one;

        /// <summary>Gets or sets a spin in degrees (a captured token tumbles home).</summary>
        public float Spin { get; set; }

        public bool Movable
        {
            get => movable;
            set
            {
                if (movable == value)
                {
                    return;
                }

                movable = value;
                pickingMode = value ? PickingMode.Position : PickingMode.Ignore;
                ring.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                ring.style.opacity = 1;
                if (value && !MotionPreferences.Reduced)
                {
                    breathing.Resume();
                }
                else
                {
                    breathing.Pause();
                }

                MarkDirtyRepaint(); // the lifted shadow
                Relayout();
            }
        }

        /// <summary>Gets or sets a value indicating whether this token waits in the background while the player picks another.</summary>
        public bool Dimmed
        {
            set => style.opacity = value ? 0.72f : 1f;
        }

        /// <summary>Shows "×n" when several of this seat's tokens share the square (on the top one only).</summary>
        public void SetStack(int count)
        {
            stack.text = "×" + count;
            stack.style.display = count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Places the element; <paramref name="origin"/> is the grid's top-left inside the board. Tokens resting in their
        /// yard are drawn larger, so the yards read as full of pieces rather than as empty wells.
        /// </summary>
        public void Layout(float cellSize, Vector2 origin, Vector2 offset, bool inYard = false)
        {
            lastCell = cellSize;
            lastOrigin = origin;
            lastOffset = offset;
            lastInYard = inYard;
            var piece = cellSize * (inYard ? 1.25f : 0.9f);
            if (Mathf.Abs(piece - pieceSize) > 0.01f)
            {
                pieceSize = piece;
                MarkDirtyRepaint();
            }

            var size = Mathf.Max(pieceSize * 1.3f, TouchTarget); // room for the ring and the lifted shadow
            style.width = size;
            style.height = size;
            style.left = origin.x + ((Cell.x + offset.x) * cellSize) - (size / 2);
            style.top = origin.y + ((Cell.y + offset.y) * cellSize) - (size / 2);
            var ringSize = pieceSize * 1.12f;
            ring.style.width = ringSize;
            ring.style.height = ringSize;
            ring.style.left = (size - ringSize) / 2;
            ring.style.top = (size - ringSize) / 2;
            var lift = (movable ? 1.08f : 1f) * (pressed ? GameVerseTokens.Motion.PressScale : 1f);
            style.scale = new Scale(new Vector3(Squash.x * lift, Squash.y * lift, 1));
            style.rotate = new Rotate(new Angle(Spin, AngleUnit.Degree));
        }

        private void Relayout()
        {
            if (lastCell > 0)
            {
                Layout(lastCell, lastOrigin, lastOffset, lastInYard);
            }
        }

        private void SetPressed(bool down)
        {
            if (pressed != down && (movable || !down))
            {
                pressed = down;
                Relayout();
            }
        }

        private void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D;
            var r = (pieceSize > 0 ? pieceSize : contentRect.width / 1.3f) / 2;
            var centre = contentRect.center;
            var seat = LudoPalette.Of(color);

            // Contact shadow: longer and softer when the piece is lifted.
            var drop = movable ? r * 0.3f : r * 0.16f;
            Paint.FillEllipse(p, centre + new Vector2(0, drop + (r * 0.55f)), r * 0.82f, r * 0.3f, movable ? LudoPalette.ShadowStrong : LudoPalette.ShadowSoft);

            Paint.FillCircle(p, centre, r * 0.86f, LudoPalette.Darken(seat, 0.42f));              // rim
            Paint.FillCircle(p, centre + new Vector2(0, -r * 0.04f), r * 0.76f, seat);             // body
            Paint.FillCircle(p, centre + new Vector2(-r * 0.12f, -r * 0.18f), r * 0.5f, LudoPalette.WithAlpha(LudoPalette.Lighten(seat, 0.35f), 0.55f)); // lit side
            Paint.FillCircle(p, centre + new Vector2(0, -r * 0.04f), r * 0.44f, LudoPalette.Lighten(seat, 0.12f)); // emblem face

            p.fillColor = LudoPalette.Ink;
            LudoPalette.FillShape(p, LudoPalette.ShapeOf(color), centre + new Vector2(0, -r * 0.04f), r * 0.3f);

            // Specular glint, top left.
            Paint.StrokeArc(p, centre + new Vector2(0, -r * 0.04f), r * 0.64f, 200, 250, LudoPalette.WithAlpha(LudoPalette.Light, 0.55f), Mathf.Max(1.5f, r * 0.1f));
        }
    }

    /// <summary>
    /// The die: a rounded cube with a soft shadow, a shaded side bevel, and pips with a little depth. While this device may
    /// roll, a volt glow breathes around it; a six flashes it.
    /// </summary>
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

        private readonly VisualElement ring = new VisualElement();
        private readonly IVisualElementScheduledItem breathing;
        private int value = 6;
        private bool active;
        private bool flashing;
        private Color accent = GameVerseTokens.Color.Text.Muted;

        public DiceElement()
        {
            name = "dice";
            generateVisualContent += Draw;

            // One crisp volt ring (a styled child), breathing by opacity: no halo layers, no per-frame repaint.
            ring.AddToClassList("ludo__dice-ring");
            ring.pickingMode = PickingMode.Ignore;
            ring.style.opacity = 0;
            Add(ring);
            breathing = schedule.Execute(() =>
            {
                if (!flashing)
                {
                    ring.style.opacity = 0.45f + (0.55f * Mathf.Sin(Time.realtimeSinceStartup * Mathf.PI * 2 / GameVerseTokens.Motion.Ambient.Duration));
                }
            }).Every(33);
            breathing.Pause();
        }

        /// <summary>Gets or sets the face shown (1 to 6).</summary>
        public int Value
        {
            get => value;
            set
            {
                this.value = Mathf.Clamp(value, 0, 6);
                MarkDirtyRepaint();
            }
        }

        /// <summary>Gets or sets a value indicating whether the die waits for this device's tap.</summary>
        public bool Active
        {
            get => active;
            set
            {
                active = value;
                pickingMode = value ? PickingMode.Position : PickingMode.Ignore;
                if (value && !MotionPreferences.Reduced)
                {
                    breathing.Resume();
                }
                else
                {
                    breathing.Pause();
                }

                if (!flashing)
                {
                    ring.style.opacity = value ? 1 : 0;
                }

                MarkDirtyRepaint();
            }
        }

        /// <summary>Gets or sets the seat colour of the player rolling (the die's rim takes it).</summary>
        public Color Accent
        {
            get => accent;
            set
            {
                accent = value;
                MarkDirtyRepaint();
            }
        }

        /// <summary>A volt flash for a six. Under reduced motion the ring simply shows for a beat.</summary>
        public void FlashSix() => FlashAsync().Forget();

        private async UniTaskVoid FlashAsync()
        {
            flashing = true;
            ring.style.opacity = 1;
            if (MotionPreferences.Reduced)
            {
                await UniTask.Delay((int)(GameVerseTokens.Motion.Slow.Duration * 1500), ignoreTimeScale: true);
            }
            else
            {
                await Tween.Run(GameVerseTokens.Motion.Slow.Duration * 1.5f, Tween.EaseOut, t => ring.style.opacity = 1 - (t * (active ? 0 : 1)));
            }

            flashing = false;
            ring.style.opacity = active ? 1 : 0;
        }

        private void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D;
            var size = Mathf.Min(contentRect.width, contentRect.height);
            var face = new Rect(size * 0.12f, size * 0.08f, size * 0.76f, size * 0.76f);
            var radius = size * 0.17f;

            Paint.SoftShadow(p, new Rect(face.x, face.y + (size * 0.06f), face.width, face.height), radius, size * 0.05f, size * 0.03f, LudoPalette.ShadowStrong);
            Paint.FillRoundRect(p, new Rect(face.x, face.y + (size * 0.06f), face.width, face.height), radius, LudoPalette.Darken(LudoPalette.Light, 0.3f)); // the side bevel
            Paint.FillRoundRect(p, face, radius, LudoPalette.Light);
            Paint.FillRoundRect(p, new Rect(face.x + (size * 0.05f), face.y + (size * 0.03f), face.width - (size * 0.1f), size * 0.05f), size * 0.025f, LudoPalette.WithAlpha(LudoPalette.Base, 0.04f));
            Paint.StrokeRoundRect(p, face, radius, LudoPalette.WithAlpha(accent, active ? 0.9f : 0.35f), Mathf.Max(1.5f, size * 0.03f));

            foreach (var pip in Pips[value])
            {
                var at = new Vector2(face.x + (pip.x * face.width), face.y + (pip.y * face.height));
                Paint.FillCircle(p, at + new Vector2(0, size * 0.008f), size * 0.062f, LudoPalette.WithAlpha(LudoPalette.Light, 0.9f));
                Paint.FillCircle(p, at, size * 0.06f, LudoPalette.Base);
            }
        }
    }

    /// <summary>A seat's token shape in ink, for avatars: names like "Player 2" all start with P, shapes do not.</summary>
    public sealed class ShapeBadge : VisualElement
    {
        private readonly int color;

        public ShapeBadge(int color)
        {
            this.color = color;
            pickingMode = PickingMode.Ignore;
            style.width = Length.Percent(60);
            style.height = Length.Percent(60);
            generateVisualContent += context =>
            {
                context.painter2D.fillColor = LudoPalette.Ink;
                LudoPalette.FillShape(context.painter2D, LudoPalette.ShapeOf(this.color), contentRect.center, contentRect.width / 2);
            };
        }
    }
}
