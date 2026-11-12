using System.Net;
using GameVerse.Infrastructure;
using GameVerse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GameVerse.Api.IntegrationTests;

public class HostingTests(StackFixture stack)
{
    [Fact]
    public async Task OpenApi_document_is_served_in_development()
    {
        await using var factory = new GameVerseApiFactory(stack);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("\"openapi\"");
    }

    [Fact]
    public async Task Hangfire_dashboard_is_denied_to_non_local_requests()
    {
        // The in-memory test server has no remote IP, so it counts as non-local.
        await using var factory = new GameVerseApiFactory(stack);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/hangfire", UriKind.Relative), TestContext.Current.CancellationToken);

        ((int)response.StatusCode).ShouldBeOneOf(401, 403);
    }

    [Fact]
    public async Task Migrations_apply_and_are_recorded()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new GameVerseApiFactory(stack);

        await factory.Services.MigrateDatabaseAsync(ct);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameVerseDbContext>();
        var applied = await db.Database.GetAppliedMigrationsAsync(ct);
        applied.ShouldContain(m => m.EndsWith("_Initial", StringComparison.Ordinal));
        (await db.Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Worker_role_runs_the_job_server_and_serves_only_health()
    {
        await using var factory = new GameVerseApiFactory(stack, new Dictionary<string, string?> { ["GameVerse:Role"] = "worker" });
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        (await client.GetAsync(new Uri("/health", UriKind.Relative), ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync(new Uri("/ws/echo", UriKind.Relative), ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        factory.Services.GetServices<IHostedService>()
            .ShouldContain(s => s.GetType().Name.Contains("BackgroundJobServer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Api_role_does_not_run_the_job_server()
    {
        await using var factory = new GameVerseApiFactory(stack);
        _ = factory.CreateClient();

        factory.Services.GetServices<IHostedService>()
            .ShouldNotContain(s => s.GetType().Name.Contains("BackgroundJobServer", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_role_fails_fast()
    {
        using var factory = new GameVerseApiFactory(stack, new Dictionary<string, string?> { ["GameVerse:Role"] = "both" });

        Should.Throw<InvalidOperationException>(() => factory.CreateClient()).Message.ShouldContain("must be 'api' or 'worker'");
    }

    [Fact]
    public void Missing_connection_strings_fail_fast_with_a_clear_message()
    {
        using var factory = new GameVerseApiFactory(stack, new Dictionary<string, string?> { ["GameVerse:ConnectionStrings:Redis"] = "" });

        Should.Throw<InvalidOperationException>(() => factory.CreateClient()).Message.ShouldContain("GameVerse__ConnectionStrings__Redis");
    }
}
