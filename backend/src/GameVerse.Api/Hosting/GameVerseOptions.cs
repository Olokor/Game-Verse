using GameVerse.Infrastructure;

namespace GameVerse.Api.Hosting;

/// <summary>
/// Everything under the "GameVerse" configuration section. Environment variables map onto it with
/// double underscores, e.g. GameVerse__ConnectionStrings__Postgres or GameVerse__Role.
/// </summary>
internal sealed class GameVerseOptions
{
    public const string Section = "GameVerse";

    /// <summary>"api" (HTTP + WebSockets) or "worker" (Hangfire job server). Same image, different role.</summary>
    public string Role { get; set; } = HostRoles.Api;

    public ConnectionStringsOptions ConnectionStrings { get; set; } = new();

    public HangfireOptions Hangfire { get; set; } = new();

    public LogOptions Log { get; set; } = new();

    public bool IsWorker => string.Equals(Role, HostRoles.Worker, StringComparison.OrdinalIgnoreCase);

    public void Validate()
    {
        if (!string.Equals(Role, HostRoles.Api, StringComparison.OrdinalIgnoreCase) && !IsWorker)
        {
            throw new InvalidOperationException("GameVerse:Role must be 'api' or 'worker', got '" + Role + "'.");
        }
    }
}

internal sealed class HangfireOptions
{
    /// <summary>Map the dashboard at /hangfire (api role only). Access rules: <see cref="HangfireDashboardAuth"/>.</summary>
    public bool Dashboard { get; set; }
}

internal sealed class LogOptions
{
    /// <summary>Human-readable console output instead of JSON. Development convenience only.</summary>
    public bool Pretty { get; set; }
}

internal static class HostRoles
{
    public const string Api = "api";
    public const string Worker = "worker";
}
