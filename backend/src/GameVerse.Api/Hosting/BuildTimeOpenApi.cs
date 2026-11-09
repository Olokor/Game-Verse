using System.Reflection;
using GameVerse.Infrastructure;

namespace GameVerse.Api.Hosting;

/// <summary>
/// Detects the build-time OpenAPI generator (Microsoft.Extensions.ApiDescription.Server, used by `make schema`).
/// It runs Program's Main inside its own process, "GetDocument.Insider", without our environment variables.
/// </summary>
internal static class BuildTimeOpenApi
{
    public static bool IsRunning => Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    public static ConnectionStringsOptions PlaceholderConnections() => new()
    {
        Postgres = "Host=openapi-build-time;Database=none",
        Redis = "openapi-build-time:6379",
    };
}
