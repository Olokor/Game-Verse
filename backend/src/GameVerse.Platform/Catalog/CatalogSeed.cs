using GameVerse.Contracts.Catalog;
using static GameVerse.Contracts.Catalog.ModeKinds;

namespace GameVerse.Platform.Catalog;

/// <summary>
/// The launch library (CLAUDE.md section 1, modes from docs/game-design/GAME_BRIEFS.md), in build order. Seeded once:
/// rows that already exist are never overwritten, so admin changes to status or text survive restarts (ADR-0007).
/// Only Ludo is playable at launch; the rest show as "Coming soon" (owner decision 2026-11-12).
/// </summary>
internal static class CatalogSeed
{
    public static IReadOnlyList<SeedGame> Games { get; } =
    [
        new("ludo", "Ludo", "Roll, race, capture. 2 to 4 players.", "board", GameStatuses.Enabled,
        [
            new("pass-and-play", Local, "Pass and play", [2, 3, 4]),
            new("vs-ai", VsAi, "vs AI", [2, 3, 4]),
            new("quick-match", OnlineRanked, "Quick match", [2, 4]),
            new("private-room", OnlinePrivate, "Private room", [2, 3, 4]),
        ]),
        new("whot", "Whot", "The Naija card classic. Hold on, pick two.", "cards", GameStatuses.ComingSoon,
        [
            new("vs-ai", VsAi, "vs AI", [2, 3, 4]),
            new("quick-match", OnlineRanked, "Quick match", [2, 3, 4]),
            new("private-room", OnlinePrivate, "Private room", [2, 3, 4]),
        ]),
        new("draughts", "Draughts", "Jump, crown, clear the board.", "board", GameStatuses.ComingSoon,
        [
            new("pass-and-play", Local, "Pass and play", [2]),
            new("vs-ai", VsAi, "vs AI", [2]),
            new("ranked", OnlineRanked, "Ranked", [2]),
            new("private-room", OnlinePrivate, "Private room", [2]),
        ]),
        new("chess", "Chess", "Rated games with clocks.", "brain", GameStatuses.ComingSoon,
        [
            new("bullet-1-0", OnlineRanked, "Bullet 1+0", [2]),
            new("blitz-3-2", OnlineRanked, "Blitz 3+2", [2]),
            new("rapid-10-0", OnlineRanked, "Rapid 10+0", [2]),
            new("vs-ai", VsAi, "vs AI", [2]),
            new("private-room", OnlinePrivate, "Private room", [2]),
            new("daily-puzzle", Solo, "Daily puzzle", [1]),
        ]),
        new("twin-lanes", "Twin Lanes", "Two cars, one brain.", "arcade", GameStatuses.ComingSoon,
        [
            new("classic", Solo, "Classic", [1]),
            new("rush", Solo, "Rush", [1]),
            new("duo", Local, "Duo", [2]),
        ]),
        new("wahala", "Wahala", "Every level lies to you.", "adventure", GameStatuses.ComingSoon,
        [
            new("campaign", Solo, "Campaign", [1]),
        ]),
        // Working title. CLAUDE.md section 1 forbids the trademarked "8 Ball Pool"; the final name is the owner's call.
        new("pool", "Pool", "Sink your set, then the eight.", "arcade", GameStatuses.ComingSoon,
        [
            new("vs-ai", VsAi, "Practice vs AI", [2]),
            new("quick-match", OnlineRanked, "1v1", [2]),
            new("private-room", OnlinePrivate, "Private room", [2]),
            new("trick-shots", Solo, "Trick shots", [1]),
        ]),
        new("downhill", "Downhill", "Race your own ghost to the bottom.", "racing", GameStatuses.ComingSoon,
        [
            new("career", Solo, "Career", [1]),
            new("ghost-race", Solo, "Ghost race", [1]),
            new("daily-track", Solo, "Daily track", [1]),
        ]),
        new("stickman-combat", "Stickman Combat", "Combo through the campaign.", "fight", GameStatuses.ComingSoon,
        [
            new("campaign", Solo, "Campaign", [1]),
            new("endless-arena", Solo, "Endless arena", [1]),
        ]),
        new("super-race", "Fabulous Super Race", "Race through worlds with personality.", "racing", GameStatuses.ComingSoon,
        [
            new("championship", Solo, "Championship", [1]),
            new("ghost-race", Solo, "Ghost race", [1]),
        ]),
        new("rival-realms", "Rival Realms", "One on one, with an original roster.", "fight", GameStatuses.ComingSoon,
        [
            new("story", Solo, "Story ladder", [1]),
            new("vs-ai", VsAi, "vs AI", [2]),
            new("versus", Local, "Local versus", [2]),
            new("training", Solo, "Training", [1]),
        ]),
    ];

    internal sealed record SeedGame(string Id, string Title, string Tagline, string Category, string Status, IReadOnlyList<SeedMode> Modes)
    {
        /// <summary>Accent = the category color token; per-game accents can be set later without a schema change.</summary>
        public string AccentToken => "color.category." + Category;
    }

    internal sealed record SeedMode(string Key, string Kind, string Label, int[] PlayerCounts);
}
