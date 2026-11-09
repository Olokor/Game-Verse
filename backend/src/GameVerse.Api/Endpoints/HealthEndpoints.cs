using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GameVerse.Api.Endpoints;

/// <summary>/health: 200 when Postgres and Redis are reachable, 503 otherwise, with a JSON breakdown.</summary>
internal static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapGameVerseHealth(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteJsonAsync,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        });
        return app;
    }

    private static Task WriteJsonAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var body = new
        {
            status = report.Status.ToString(),
            durationMs = (long)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString(),
                    durationMs = (long)e.Value.Duration.TotalMilliseconds,

                    // Exception details stay in the logs, not in a public response.
                    error = e.Value.Status == HealthStatus.Healthy ? null : "unreachable",
                }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(body));
    }
}
