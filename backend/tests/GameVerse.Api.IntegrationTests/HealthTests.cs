using System.Net;
using System.Text.Json;

namespace GameVerse.Api.IntegrationTests;

public class HealthTests(StackFixture stack)
{
    [Fact]
    public async Task Health_is_200_when_postgres_and_redis_are_up()
    {
        await using var factory = new GameVerseApiFactory(stack);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        json.RootElement.GetProperty("status").GetString().ShouldBe("Healthy");
        var checks = json.RootElement.GetProperty("checks");
        checks.GetProperty("postgres").GetProperty("status").GetString().ShouldBe("Healthy");
        checks.GetProperty("redis").GetProperty("status").GetString().ShouldBe("Healthy");
    }

    [Fact]
    public async Task Health_is_503_and_names_the_failing_dependency_when_redis_is_down()
    {
        // Nothing listens on port 1, so the Redis check fails while Postgres stays healthy.
        await using var factory = new GameVerseApiFactory(stack, new Dictionary<string, string?>
        {
            ["GameVerse:ConnectionStrings:Redis"] = "127.0.0.1:1,abortConnect=false,connectTimeout=500",
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        json.RootElement.GetProperty("status").GetString().ShouldBe("Unhealthy");
        var checks = json.RootElement.GetProperty("checks");
        checks.GetProperty("postgres").GetProperty("status").GetString().ShouldBe("Healthy");
        checks.GetProperty("redis").GetProperty("status").GetString().ShouldBe("Unhealthy");
        checks.GetProperty("redis").GetProperty("error").GetString().ShouldBe("unreachable");
    }
}
