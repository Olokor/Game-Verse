using UnityEngine.UIElements;

namespace GameVerse.UI
{
    public enum ButtonVariant
    {
        Primary,
        Secondary,
        Ghost,
        Danger,
    }

    public enum ButtonSize
    {
        Sm,
        Md,
        Lg,
    }

    /// <summary>
    /// Applies the design-system button look to a UI Toolkit <see cref="Button"/>, matching the web Button
    /// (apps/web/src/components/ui/Button.tsx): variants, sizes (all at least the 44px touch target), hover, pressed
    /// (scale to motion.press-scale), focus, disabled and loading. In UXML, add the classes directly:
    /// class="gv-button gv-button--primary gv-button--md".
    /// </summary>
    public static class ButtonStyle
    {
        public const string UssClass = "gv-button";
        public const string LoadingClass = "gv-button--loading";

        public static void Apply(Button button, ButtonVariant variant, ButtonSize size, bool pill = false)
        {
            button.AddToClassList(UssClass);
            foreach (var v in new[] { "primary", "secondary", "ghost", "danger" })
            {
                button.RemoveFromClassList(UssClass + "--" + v);
            }

            foreach (var s in new[] { "sm", "md", "lg" })
            {
                button.RemoveFromClassList(UssClass + "--" + s);
            }

            button.AddToClassList(UssClass + "--" + variant.ToString().ToLowerInvariant());
            button.AddToClassList(UssClass + "--" + size.ToString().ToLowerInvariant());
            button.EnableInClassList(UssClass + "--pill", pill);
        }

        /// <summary>
        /// Loading: keeps the button's size, swaps the label for three dots and ignores clicks.
        /// Unlike SetEnabled(false), the button keeps its normal color, matching web.
        /// </summary>
        public static void SetLoading(Button button, bool loading)
        {
            button.EnableInClassList(LoadingClass, loading);
            button.pickingMode = loading ? PickingMode.Ignore : PickingMode.Position;

            var dots = button.Q(className: UssClass + "__dots");
            if (loading && dots == null)
            {
                dots = new VisualElement();
                dots.AddToClassList(UssClass + "__dots");
                for (var i = 0; i < 3; i++)
                {
                    var dot = new VisualElement();
                    dot.AddToClassList(UssClass + "__dot");
                    dots.Add(dot);
                }

                button.Add(dots);
            }
        }
    }
}
