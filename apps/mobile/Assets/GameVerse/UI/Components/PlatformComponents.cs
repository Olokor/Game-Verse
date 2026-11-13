#nullable enable
using System;
using Cysharp.Threading.Tasks;
using GameVerse.UI.Motion;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameVerse.UI
{
    /// <summary>
    /// A big tappable mode tile (DESIGN_SYSTEM section 6): Quick match, Ranked, Private room, vs AI, Pass and play.
    /// Pressed scales to motion.press-scale (Components.uss); selected shows a volt outline.
    /// </summary>
    public sealed class ModeTile : Button
    {
        private readonly Label titleLabel = new Label();
        private readonly Label detailLabel = new Label();

        public ModeTile(string key, string title, string detail)
        {
            Key = key;
            AddToClassList("gv-mode");
            titleLabel.AddToClassList("gv-mode__title");
            detailLabel.AddToClassList("gv-mode__detail");
            Add(titleLabel);
            Add(detailLabel);
            Title = title;
            Detail = detail;
            clicked += Haptics.Light;
        }

        /// <summary>The catalog mode key this tile starts.</summary>
        public string Key { get; }

        public string Title
        {
            get => titleLabel.text;
            set => titleLabel.text = value;
        }

        public string Detail
        {
            get => detailLabel.text;
            set => detailLabel.text = value;
        }

        public bool Selected
        {
            get => ClassListContains("gv-mode--selected");
            set => EnableInClassList("gv-mode--selected", value);
        }
    }

    /// <summary>
    /// A player chip (DESIGN_SYSTEM section 6): avatar disc in the seat colour, name, a status line, and an online dot.
    /// In game it also shows a turn ring that empties toward the turn deadline.
    /// </summary>
    public sealed class PlayerChip : VisualElement
    {
        private readonly VisualElement avatar = new VisualElement();
        private readonly Label initial = new Label();
        private readonly Label nameLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly VisualElement dot = new VisualElement();
        private readonly VisualElement turnBar = new VisualElement();
        private readonly VisualElement turnFill = new VisualElement();

        public PlayerChip()
        {
            AddToClassList("gv-player");
            avatar.AddToClassList("gv-player__avatar");
            initial.AddToClassList("gv-player__initial");
            avatar.Add(initial);
            dot.AddToClassList("gv-player__dot");
            avatar.Add(dot);
            var text = new VisualElement();
            text.AddToClassList("gv-player__text");
            nameLabel.AddToClassList("gv-player__name");
            statusLabel.AddToClassList("gv-player__status");
            text.Add(nameLabel);
            text.Add(statusLabel);
            turnBar.AddToClassList("gv-player__turn");
            turnFill.AddToClassList("gv-player__turn-fill");
            turnBar.Add(turnFill);
            text.Add(turnBar);
            Add(avatar);
            Add(text);
            SetTurn(false, null);
        }

        /// <param name="online">Connection dot: true online, false offline, null for no dot (AI seats, empty seats).</param>
        public void Set(string displayName, string status, Color seatColor, bool? online)
        {
            nameLabel.text = displayName;
            initial.text = displayName.Length > 0 ? displayName.Substring(0, 1).ToUpperInvariant() : "?";
            statusLabel.text = status;
            statusLabel.style.display = status.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            avatar.style.backgroundColor = seatColor;
            dot.style.display = online is null ? DisplayStyle.None : DisplayStyle.Flex;
            dot.EnableInClassList("gv-player__dot--online", online == true);
        }

        /// <summary>Shows a game's seat mark (e.g. a Ludo token shape) in the avatar instead of the initial.</summary>
        public void SetGlyph(VisualElement? glyph)
        {
            avatar.Query(className: "gv-player__glyph").ForEach(g => g.RemoveFromHierarchy());
            initial.style.display = glyph is null ? DisplayStyle.Flex : DisplayStyle.None;
            if (glyph != null)
            {
                glyph.AddToClassList("gv-player__glyph");
                avatar.Insert(0, glyph);
            }
        }

        /// <summary>Highlights the seat to act; <paramref name="remaining"/> 0..1 of the turn clock left (null: no clock).</summary>
        public void SetTurn(bool active, float? remaining)
        {
            EnableInClassList("gv-player--active", active);
            turnBar.style.display = active && remaining != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (remaining is { } left)
            {
                turnFill.style.width = Length.Percent(Mathf.Clamp01(left) * 100);
                turnFill.EnableInClassList("gv-player__turn-fill--low", left < 0.3f);
            }
        }
    }

    /// <summary>The room code, big and monospaced, with Copy and Share (DESIGN_SYSTEM section 6, RoomCodeDisplay).</summary>
    public sealed class RoomCodeDisplay : VisualElement
    {
        private readonly Label code = new Label();

        public RoomCodeDisplay()
        {
            AddToClassList("gv-roomcode");
            var caption = new Label("ROOM CODE");
            caption.AddToClassList("gv-label");
            code.AddToClassList("gv-roomcode__code");
            var actions = new VisualElement();
            actions.AddToClassList("gv-roomcode__actions");
            CopyButton = new Button { text = "Copy" };
            ButtonStyle.Apply(CopyButton, ButtonVariant.Secondary, ButtonSize.Sm, pill: true);
            ShareButton = new Button { text = "Share" };
            ButtonStyle.Apply(ShareButton, ButtonVariant.Secondary, ButtonSize.Sm, pill: true);
            actions.Add(CopyButton);
            actions.Add(ShareButton);
            Add(caption);
            Add(code);
            Add(actions);
        }

        public Button CopyButton { get; }

        public Button ShareButton { get; }

        public string Code
        {
            get => code.text;
            set => code.text = value;
        }
    }

    /// <summary>A bottom sheet (DESIGN_SYSTEM section 6): a scrim and a panel that slides up; tapping the scrim closes it.</summary>
    public sealed class Sheet : VisualElement
    {
        private readonly VisualElement panel = new VisualElement();
        private readonly Label titleLabel = new Label();

        public Sheet(string title)
        {
            AddToClassList("gv-sheet");
            panel.AddToClassList("gv-sheet__panel");
            titleLabel.AddToClassList("gv-sheet__title");
            titleLabel.text = title;
            panel.Add(titleLabel);
            Content = new VisualElement();
            panel.Add(Content);
            Add(panel);
            RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.target == this)
                {
                    Close();
                }
            });
            panel.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
        }

        public VisualElement Content { get; }

        public event Action? Closed;

        /// <summary>Shows the sheet over <paramref name="host"/> with a slide-up.</summary>
        public void Open(VisualElement host)
        {
            host.Add(this);
            if (MotionPreferences.Reduced)
            {
                Tween.Fade(this).Forget();
                return;
            }

            Tween.Run(GameVerseTokens.Motion.Base.Duration, Tween.Standard, t =>
            {
                style.opacity = t;
                panel.style.translate = new Translate(0, Length.Percent((1 - t) * 30));
            }).Forget();
        }

        public void Close()
        {
            RemoveFromHierarchy();
            Closed?.Invoke();
        }
    }

    /// <summary>A short message at the bottom of the screen that fades after a few seconds (DESIGN_SYSTEM section 6).</summary>
    public static class Toast
    {
        public static void Show(VisualElement host, string message, bool error = false)
        {
            var toast = new Label(message);
            toast.AddToClassList("gv-toast");
            toast.EnableInClassList("gv-toast--error", error);
            host.Add(toast);
            HideLaterAsync(toast).Forget();
        }

        private static async UniTaskVoid HideLaterAsync(VisualElement toast)
        {
            await UniTask.Delay((int)(GameVerseTokens.Motion.ToastHold * 1000), ignoreTimeScale: true);
            await Tween.Run(GameVerseTokens.Motion.Fast.Duration, Tween.Standard, t => toast.style.opacity = 1 - t);
            toast.RemoveFromHierarchy();
        }
    }

    /// <summary>
    /// A skeleton block for loading states (DESIGN_SYSTEM: skeletons, not spinners). Pulses with motion.ambient; static
    /// under reduced motion.
    /// </summary>
    public sealed class Skeleton : VisualElement
    {
        public Skeleton(float height, float widthPercent = 100)
        {
            AddToClassList("gv-skeleton");
            style.height = height;
            style.width = Length.Percent(widthPercent);
            if (!MotionPreferences.Reduced)
            {
                schedule.Execute(() =>
                {
                    var phase = Time.realtimeSinceStartup % GameVerseTokens.Motion.Ambient.Duration / GameVerseTokens.Motion.Ambient.Duration;
                    style.opacity = 0.55f + (0.35f * Mathf.Sin(phase * Mathf.PI * 2));
                }).Every(33);
            }
        }
    }
}
