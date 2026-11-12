using System.Collections.Generic;

namespace GameVerse.Contracts.Catalog
{
    /// <summary>A game in the catalog (Prompt 1.2, ADR-0007). Shared by the web and Unity game lists.</summary>
    public class GameInfo
    {
        /// <summary>Gets or sets the game id (slug), e.g. "ludo". Also the SessionManifest gameId.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Gets or sets the display title.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Gets or sets the one-line pitch shown on cards.</summary>
        public string Tagline { get; set; } = string.Empty;

        /// <summary>Gets or sets the category: board, cards, brain, arcade, adventure, racing or fight.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the accent as a design-token path (e.g. "color.category.board"), never a color value: each
        /// client resolves it with its own generated tokens.
        /// </summary>
        public string AccentToken { get; set; } = string.Empty;

        /// <summary>Gets or sets the current game version (SessionManifest gameVersion).</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>Gets or sets the availability, one of <see cref="GameStatuses"/>.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Gets or sets the ways to play, in display order.</summary>
        public List<GameModeInfo> Modes { get; set; } = new List<GameModeInfo>();
    }

    /// <summary>One way to play a game.</summary>
    public class GameModeInfo
    {
        /// <summary>Gets or sets the mode key, unique within the game (e.g. "blitz-3-2").</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Gets or sets the kind: local, vs_ai, online_ranked, online_private or solo (CLAUDE.md 4.2).</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>Gets or sets the display label, e.g. "Quick match".</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Gets or sets the allowed player counts, humans and AI together (e.g. 2 and 4 for Ludo quick match).</summary>
        public List<int> PlayerCounts { get; set; } = new List<int>();
    }

    /// <summary>Values of <see cref="GameInfo.Status"/>: the per-game feature flag.</summary>
    public static class GameStatuses
    {
        /// <summary>Playable.</summary>
        public const string Enabled = "enabled";

        /// <summary>Listed with a "Coming soon" label, not playable.</summary>
        public const string ComingSoon = "coming_soon";

        /// <summary>Not listed at all.</summary>
        public const string Hidden = "hidden";
    }

    /// <summary>Values of <see cref="GameModeInfo.Kind"/> (same vocabulary as SessionManifest.mode).</summary>
    public static class ModeKinds
    {
        /// <summary>Several players on one device.</summary>
        public const string Local = "local";

        /// <summary>One player against AI seats.</summary>
        public const string VsAi = "vs_ai";

        /// <summary>Matchmade online play.</summary>
        public const string OnlineRanked = "online_ranked";

        /// <summary>Online play in a private room.</summary>
        public const string OnlinePrivate = "online_private";

        /// <summary>Single player (score attack, campaign, puzzle).</summary>
        public const string Solo = "solo";
    }
}
