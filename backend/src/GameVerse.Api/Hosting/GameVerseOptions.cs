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

    public ErrorReportingOptions ErrorReporting { get; set; } = new();

    /// <summary>
    /// Browser origins allowed to call the API with credentials (the web app sends the refresh cookie). Exact origins
    /// only, never "*": e.g. http://localhost:5173 in development, https://gameverse.app in production.
    /// </summary>
    public string[] CorsOrigins { get; set; } = [];

    public bool IsWorker => string.Equals(Role, HostRoles.Worker, StringComparison.OrdinalIgnoreCase);

    public void Validate()
    {
        if (!string.Equals(Role, HostRoles.Api, StringComparison.OrdinalIgnoreCase) && !IsWorker)
        {
            throw new InvalidOperationException("GameVerse:Role must be 'api' or 'worker', got '" + Role + "'.");
        }

        ErrorReporting.Validate();
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

/// <summary>
/// Error reporting through the Sentry SDK to self-hosted GlitchTip (ADR-0005). Off unless <see cref="Dsn"/> is set.
/// </summary>
internal sealed class ErrorReportingOptions
{
    /// <summary>Project DSN printed by `make errors-setup`, e.g. http://key@glitchtip:8000/1. Empty = reporting off.</summary>
    public string? Dsn { get; set; }

    /// <summary>Environment tag on every event (development, staging, production).</summary>
    public string Environment { get; set; } = "development";

    /// <summary>Release tag; defaults to the API assembly's informational version.</summary>
    public string? Release { get; set; }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(Dsn);

    /// <summary>
    /// A DSN is scheme://publicKey@host[:port]/projectId. Rejected at startup when malformed, so a typo cannot silently
    /// turn reporting off in production. The DSN itself is not echoed in the message.
    /// </summary>
    public void Validate()
    {
        if (!IsEnabled)
        {
            return;
        }

        var valid = Uri.TryCreate(Dsn, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(uri.UserInfo)
            && long.TryParse(uri.AbsolutePath.Trim('/').Split('/')[^1], out _);
        if (!valid)
        {
            throw new InvalidOperationException(
                "GameVerse:ErrorReporting:Dsn is not a valid DSN (expected http(s)://publicKey@host/projectId). Leave it empty to turn reporting off.");
        }
    }
}

internal static class HostRoles
{
    public const string Api = "api";
    public const string Worker = "worker";
}
