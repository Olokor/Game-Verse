using System.Net;
using System.Net.Http.Json;
using GameVerse.Contracts.Catalog;
using GameVerse.Infrastructure.Persistence;
using GameVerse.Platform.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GameVerse.Api.IntegrationTests.Catalog;

/// <summary>
/// The game registry (Prompt 1.2, ADR-0007). Tests in this class change catalog rows, so they run in one class
/// (xUnit runs a class's tests one at a time) and put every row back.
/// </summary>
public class CatalogTests(StackFixture stack)
{
    private static readonly string[] LaunchOrder =
        ["ludo", "whot", "draughts", "chess", "twin-lanes", "wahala", "pool", "downhill", "stickman-combat", "super-race", "rival-realms"];

    [Fact]
    public async Task The_catalog_lists_all_11_games_in_build_order_with_only_Ludo_playable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new GameVerseApiFactory(stack);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/catalog/games", UriKind.Relative), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl?.ToString().ShouldBe("public, max-age=60");
        var games = (await response.Content.ReadFromJsonAsync<List<GameInfo>>(ct)).ShouldNotBeNull();
        games.Select(g => g.Id).ShouldBe(LaunchOrder);
        games.Where(g => g.Status == GameStatuses.Enabled).Select(g => g.Id).ShouldBe(["ludo"]);
        games.Where(g => g.Id != "ludo").ShouldAllBe(g => g.Status == GameStatuses.ComingSoon);
        games.ShouldAllBe(g => g.AccentToken == "color.category." + g.Category && g.Modes.Count > 0);
        games.ShouldNotContain(g => g.Title.Contains("8 Ball", StringComparison.OrdinalIgnoreCase), "CLAUDE.md: no third-party trademarks");
    }

    [Fact]
    public async Task Ludo_has_the_four_modes_from_the_brief()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new GameVerseApiFactory(stack);
        using var client = factory.CreateClient();

        var ludo = (await client.GetFromJsonAsync<GameInfo>(new Uri("/catalog/games/ludo", UriKind.Relative), ct)).ShouldNotBeNull();

        ludo.Modes.Select(m => (m.Key, m.Kind)).ShouldBe(
        [
            ("pass-and-play", ModeKinds.Local),
            ("vs-ai", ModeKinds.VsAi),
            ("quick-match", ModeKinds.OnlineRanked),
            ("private-room", ModeKinds.OnlinePrivate),
        ]);
        ludo.Modes.Single(m => m.Key == "quick-match").PlayerCounts.ShouldBe([2, 4]);
    }

    [Fact]
    public async Task An_unknown_game_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new GameVerseApiFactory(stack);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/catalog/games/minecraft", UriKind.Relative), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task A_hidden_game_disappears_from_the_list_and_its_page()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new GameVerseApiFactory(stack);
        using var client = factory.CreateClient();
        await SetStatusAsync(factory, "rival-realms", GameStatuses.Hidden, ct);
        try
        {
            var games = (await client.GetFromJsonAsync<List<GameInfo>>(new Uri("/catalog/games", UriKind.Relative), ct)).ShouldNotBeNull();
            games.ShouldNotContain(g => g.Id == "rival-realms");
            using var page = await client.GetAsync(new Uri("/catalog/games/rival-realms", UriKind.Relative), ct);
            page.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        finally
        {
            await SetStatusAsync(factory, "rival-realms", GameStatuses.ComingSoon, ct);
        }
    }

    [Fact]
    public async Task Seeding_again_inserts_nothing_and_keeps_an_edited_status()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new GameVerseApiFactory(stack);
        await SetStatusAsync(factory, "wahala", GameStatuses.Enabled, ct);
        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GameVerseDbContext>();
            await CatalogModule.SeedAsync(db, ct);
            await CatalogModule.SeedAsync(db, ct);

            (await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM catalog.games").SingleAsync(ct)).ShouldBe(11);
            (await db.Database.SqlQuery<string>($"SELECT status AS \"Value\" FROM catalog.games WHERE id = 'wahala'").SingleAsync(ct))
                .ShouldBe(GameStatuses.Enabled, "an admin's change survives re-seeding");
        }
        finally
        {
            await SetStatusAsync(factory, "wahala", GameStatuses.ComingSoon, ct);
        }
    }

    private static async Task SetStatusAsync(GameVerseApiFactory factory, string gameId, string status, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameVerseDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE catalog.games SET status = {status} WHERE id = {gameId}", ct);
    }
}
