using System.Reflection;
using Sentry;
using Sentry.AspNetCore;
using Sentry.Extensibility;
using Serilog;
using Serilog.Events;

namespace GameVerse.Api.Hosting;

/// <summary>
/// Error reporting (ADR-0005): the Sentry SDK, pointed at self-hosted GlitchTip. Only wired when a DSN is configured.
/// Privacy: no PII (no IPs, cookies, headers or bodies). Events carry the exception, the request route and the trace id.
/// Logging stays Serilog's job; error-level log events are forwarded to the same DSN by the Sentry Serilog sink.
/// </summary>
internal static class ErrorReporting
{
    public static void ConfigureSdk(SentryAspNetCoreOptions sentry, ErrorReportingOptions options)
    {
        sentry.Dsn = options.Dsn;
        sentry.Environment = options.Environment;
        sentry.Release = options.Release
            ?? typeof(ErrorReporting).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        sentry.SendDefaultPii = false;
        sentry.MaxRequestBodySize = RequestSize.None;
        sentry.AttachStacktrace = true;
        sentry.TracesSampleRate = 0; // tracing belongs to OpenTelemetry (CLAUDE.md section 3), not to error reporting
        sentry.SetBeforeSend((sentryEvent, _) => Scrub(sentryEvent));
    }

    /// <summary>
    /// Last line of defence before an event leaves the server: no request headers (user agents, forwarded IPs, auth),
    /// no cookies and no query strings (WebSocket tickets travel as ?ticket=..., CLAUDE.md section 4.5).
    /// The route path and method stay, because they identify the failing endpoint.
    /// </summary>
    public static SentryEvent Scrub(SentryEvent sentryEvent)
    {
        var request = sentryEvent.Request;
        request.Headers.Clear();
        request.Cookies = null;
        request.QueryString = null;
        request.Data = null;
        if (request.Url is { } url && url.IndexOf('?', StringComparison.Ordinal) is var q and >= 0)
        {
            request.Url = url[..q];
        }

        return sentryEvent;
    }

    /// <summary>Error and fatal log events become reported events; information and above become breadcrumbs.</summary>
    public static void AddSerilogSink(LoggerConfiguration logger)
    {
        logger.WriteTo.Sentry(sink =>
        {
            sink.InitializeSdk = false; // UseSentry already initialised the SDK with the options above
            sink.MinimumEventLevel = LogEventLevel.Error;
            sink.MinimumBreadcrumbLevel = LogEventLevel.Information;
        });
    }
}
