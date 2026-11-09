namespace GameVerse.Platform;

/// <summary>
/// The canonical list of platform modules (CLAUDE.md 4.1). Each is a folder and namespace under
/// GameVerse.Platform. Architecture tests check that folders, namespaces and this list agree, and
/// that no module uses another module's internal types.
/// </summary>
public static class PlatformModules
{
    public static IReadOnlyList<string> All { get; } =
    [
        "Accounts",
        "Profiles",
        "Catalog",
        "Sessions",
        "Matchmaking",
        "Realtime",
        "Social",
        "Progression",
        "Economy",
        "Tournaments",
        "Leaderboards",
        "Notifications",
        "Moderation",
        "Admin",
    ];
}
