using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GameVerse.Api.IntegrationTests;

/// <summary>
/// Hosts the real API in-process against the Testcontainers stack. Settings are passed with UseSetting,
/// which the API reads through its normal configuration (same keys as the GameVerse__ environment variables).
/// </summary>
public sealed class GameVerseApiFactory(StackFixture stack, IReadOnlyDictionary<string, string?>? overrides = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("GameVerse:ConnectionStrings:Postgres", stack.PostgresConnectionString);
        builder.UseSetting("GameVerse:ConnectionStrings:Redis", stack.RedisConnectionString);
        builder.UseSetting("GameVerse:Log:Pretty", "true");

        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }
    }
}
