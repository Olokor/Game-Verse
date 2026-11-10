using UnityEngine.UIElements;

namespace GameVerse.UI
{
    /// <summary>
    /// Game card, matching the web component (apps/web/src/components/ui/GameCard.tsx): hero (wide, featured, Play label),
    /// standard (portrait 3:4), compact (row). Styling lives in Components.uss and uses only token variables.
    /// Usable from UXML: &lt;GameVerse.UI.GameCard variant="hero" title="Ludo" category="board" ... /&gt;.
    /// TODO(gameverse): key art per game replaces the placeholder glow (DESIGN_SYSTEM.md section 8).
    /// </summary>
    [UxmlElement]
    public partial class GameCard : VisualElement
    {
        public const string UssClass = "gv-card";

        private readonly VisualElement art = new VisualElement { name = "art" };
        private readonly VisualElement glyph = new VisualElement { name = "glyph" };
        private readonly VisualElement chips = new VisualElement { name = "chips" };
        private readonly Label categoryChip = new Label { name = "category" };
        private readonly Label badgeChip = new Label { name = "badge" };
        private readonly VisualElement body = new VisualElement { name = "body" };
        private readonly Label eyebrow = new Label("FEATURED") { name = "eyebrow" };
        private readonly Label titleLabel = new Label { name = "title" };
        private readonly Label subtitleLabel = new Label { name = "subtitle" };
        private readonly VisualElement actions = new VisualElement { name = "actions" };
        private readonly Button play = new Button { name = "play", text = "Play now" };
        private readonly Label live = new Label { name = "live" };

        private string variant = "standard";
        private string category = "board";
        private string badge = string.Empty;
        private int livePlayers;

        public GameCard()
        {
            AddToClassList(UssClass);
            focusable = true;
            pickingMode = PickingMode.Position;

            art.AddToClassList(UssClass + "__art");
            glyph.AddToClassList(UssClass + "__glyph");
            chips.AddToClassList(UssClass + "__chips");
            categoryChip.AddToClassList("gv-chip");
            badgeChip.AddToClassList("gv-chip");
            body.AddToClassList(UssClass + "__body");
            eyebrow.AddToClassList("gv-label");
            eyebrow.AddToClassList(UssClass + "__eyebrow");
            titleLabel.AddToClassList(UssClass + "__title");
            subtitleLabel.AddToClassList(UssClass + "__subtitle");
            actions.AddToClassList(UssClass + "__actions");
            ButtonStyle.Apply(play, ButtonVariant.Primary, ButtonSize.Md);
            live.AddToClassList(UssClass + "__live");

            art.Add(glyph);
            chips.Add(categoryChip);
            chips.Add(badgeChip);
            art.Add(chips);
            actions.Add(play);
            actions.Add(live);
            body.Add(eyebrow);
            body.Add(titleLabel);
            body.Add(subtitleLabel);
            body.Add(actions);
            Add(art);
            Add(body);

            Refresh();
        }

        /// <summary>hero, standard or compact.</summary>
        [UxmlAttribute]
        public string Variant
        {
            get => variant;
            set { variant = value; Refresh(); }
        }

        [UxmlAttribute]
        public string Title
        {
            get => titleLabel.text;
            set => titleLabel.text = value;
        }

        /// <summary>Tagline or status line, e.g. "vs Tobi".</summary>
        [UxmlAttribute]
        public string Subtitle
        {
            get => subtitleLabel.text;
            set => subtitleLabel.text = value;
        }

        /// <summary>board, cards, arcade, racing, fight, adventure or brain. Drives the glow color token.</summary>
        [UxmlAttribute]
        public string Category
        {
            get => category;
            set { category = value; Refresh(); }
        }

        /// <summary>"new", "event" or empty.</summary>
        [UxmlAttribute]
        public string Badge
        {
            get => badge;
            set { badge = value ?? string.Empty; Refresh(); }
        }

        [UxmlAttribute]
        public int LivePlayers
        {
            get => livePlayers;
            set { livePlayers = value; Refresh(); }
        }

        /// <summary>The Play label inside hero cards.</summary>
        public Button PlayButton => play;

        private void Refresh()
        {
            foreach (var v in new[] { "hero", "standard", "compact" })
            {
                EnableInClassList(UssClass + "--" + v, v == variant);
            }

            foreach (var c in new[] { "board", "cards", "arcade", "racing", "fight", "adventure", "brain" })
            {
                EnableInClassList("gv-category--" + c, c == category);
            }

            categoryChip.text = category.ToUpperInvariant();
            badgeChip.text = badge.ToUpperInvariant();
            badgeChip.EnableInClassList("gv-chip--new", badge == "new");
            badgeChip.EnableInClassList("gv-chip--event", badge == "event");
            badgeChip.style.display = badge.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            var hero = variant == "hero";
            eyebrow.style.display = hero ? DisplayStyle.Flex : DisplayStyle.None;
            actions.style.display = hero ? DisplayStyle.Flex : DisplayStyle.None;
            chips.style.display = variant == "compact" ? DisplayStyle.None : DisplayStyle.Flex;
            live.text = FormatCompact(livePlayers) + " playing";
        }

        // Same output as the web's Intl compact format for the ranges we show ("12.8K").
        private static string FormatCompact(int n)
        {
            if (n >= 1_000_000) return (n / 1_000_000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "M";
            if (n >= 1_000) return (n / 1_000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "K";
            return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
