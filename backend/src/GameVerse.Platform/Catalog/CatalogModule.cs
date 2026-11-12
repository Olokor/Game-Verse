using GameVerse.Contracts.Catalog;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace GameVerse.Platform.Catalog;

/// <summary>What other modules may ask the catalog (CLAUDE.md 4.1). Sessions and Matchmaking refuse unplayable games.</summary>
public interface ICatalog
{
    /// <summary>The game, including coming-soon ones; null when unknown or hidden.</summary>
    Task<GameInfo?> GetGameAsync(string gameId, CancellationToken ct = default);
}

internal sealed class Game
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Tagline { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string AccentToken { get; set; } = string.Empty;

    public string Version { get; set; } = "0.1.0";

    public int SortOrder { get; set; }

    /// <summary>enabled, coming_soon or hidden (GameStatuses): the per-game feature flag.</summary>
    public string Status { get; set; } = GameStatuses.ComingSoon;

    public List<GameMode> Modes { get; set; } = [];
}

internal sealed class GameMode
{
    public string GameId { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public int[] PlayerCounts { get; set; } = [];

    public int SortOrder { get; set; }
}

internal sealed class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> builder)
    {
        builder.ToTable("games", "catalog");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasMaxLength(40);
        builder.Property(g => g.Title).HasMaxLength(60).IsRequired();
        builder.Property(g => g.Tagline).HasMaxLength(120).IsRequired();
        builder.Property(g => g.Category).HasMaxLength(20).IsRequired();
        builder.Property(g => g.AccentToken).HasMaxLength(60).IsRequired();
        builder.Property(g => g.Version).HasMaxLength(20).IsRequired();
        builder.Property(g => g.Status).HasMaxLength(20).IsRequired();
        builder.HasMany(g => g.Modes).WithOne().HasForeignKey(m => m.GameId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class GameModeConfiguration : IEntityTypeConfiguration<GameMode>
{
    public void Configure(EntityTypeBuilder<GameMode> builder)
    {
        builder.ToTable("game_modes", "catalog");
        builder.HasKey(m => new { m.GameId, m.Key });
        builder.Property(m => m.Key).HasMaxLength(40);
        builder.Property(m => m.Kind).HasMaxLength(20).IsRequired();
        builder.Property(m => m.Label).HasMaxLength(40).IsRequired();
    }
}

internal sealed class CatalogService(DbContext db) : ICatalog
{
    public async Task<IReadOnlyList<GameInfo>> ListAsync(CancellationToken ct)
    {
        var games = await db.Set<Game>().AsNoTracking().Include(g => g.Modes)
            .Where(g => g.Status != GameStatuses.Hidden)
            .OrderBy(g => g.SortOrder)
            .ToListAsync(ct).ConfigureAwait(false);
        return games.Select(ToInfo).ToList();
    }

    public async Task<GameInfo?> GetGameAsync(string gameId, CancellationToken ct = default)
    {
        var game = await db.Set<Game>().AsNoTracking().Include(g => g.Modes)
            .SingleOrDefaultAsync(g => g.Id == gameId && g.Status != GameStatuses.Hidden, ct).ConfigureAwait(false);
        return game is null ? null : ToInfo(game);
    }

    private static GameInfo ToInfo(Game g) => new()
    {
        Id = g.Id,
        Title = g.Title,
        Tagline = g.Tagline,
        Category = g.Category,
        AccentToken = g.AccentToken,
        Version = g.Version,
        Status = g.Status,
        Modes = g.Modes.OrderBy(m => m.SortOrder)
            .Select(m => new GameModeInfo { Key = m.Key, Kind = m.Kind, Label = m.Label, PlayerCounts = [.. m.PlayerCounts] })
            .ToList(),
    };
}

/// <summary>
/// Inserts the launch library rows that do not exist yet; never updates (admin edits win). Runs right after the schema
/// migrations (the `migrate` command, `make migrate`, the test fixture). Safe to run concurrently or repeatedly:
/// INSERT ... ON CONFLICT DO NOTHING.
/// </summary>
internal static class CatalogSeeder
{
    public static async Task SeedAsync(DbContext db, CancellationToken ct)
    {
        for (var i = 0; i < CatalogSeed.Games.Count; i++)
        {
            var g = CatalogSeed.Games[i];
            var order = (i + 1) * 10;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO catalog.games (id, title, tagline, category, accent_token, version, sort_order, status)
                VALUES ({g.Id}, {g.Title}, {g.Tagline}, {g.Category}, {g.AccentToken}, {"0.1.0"}, {order}, {g.Status})
                ON CONFLICT (id) DO NOTHING
                """, ct).ConfigureAwait(false);
            for (var m = 0; m < g.Modes.Count; m++)
            {
                var mode = g.Modes[m];
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO catalog.game_modes (game_id, key, kind, label, player_counts, sort_order)
                    VALUES ({g.Id}, {mode.Key}, {mode.Kind}, {mode.Label}, {mode.PlayerCounts}, {(m + 1) * 10})
                    ON CONFLICT (game_id, key) DO NOTHING
                    """, ct).ConfigureAwait(false);
            }
        }
    }
}

/// <summary>Registers the Catalog module and maps <c>/catalog/games</c>.</summary>
public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddScoped<CatalogService>();
        services.AddScoped<ICatalog>(sp => sp.GetRequiredService<CatalogService>());
        return services;
    }

    /// <summary>Seeds the launch library (idempotent). Called after migrations by Infrastructure.</summary>
    public static Task SeedAsync(DbContext db, CancellationToken ct = default) => CatalogSeeder.SeedAsync(db, ct);

    public static IEndpointRouteBuilder MapCatalog(this IEndpointRouteBuilder app)
    {
        var catalog = app.MapGroup("/catalog").WithTags("Catalog");

        catalog.MapGet("/games", async (HttpContext http, CatalogService games) =>
            {
                CacheFor60Seconds(http);
                return Results.Ok(await games.ListAsync(http.RequestAborted).ConfigureAwait(false));
            })
            .WithSummary("Every listed game with its modes (playable and coming soon)")
            .Produces<List<GameInfo>>();

        catalog.MapGet("/games/{id}", async (string id, HttpContext http, CatalogService games) =>
            {
                var game = await games.GetGameAsync(id, http.RequestAborted).ConfigureAwait(false);
                if (game is null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Game not found");
                }

                CacheFor60Seconds(http);
                return Results.Ok(game);
            })
            .WithSummary("One game with its modes")
            .Produces<GameInfo>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    // Public data that changes rarely (an admin flips a status): a short shared cache keeps Home fast on slow networks.
    private static void CacheFor60Seconds(HttpContext http) => http.Response.Headers.CacheControl = "public, max-age=60";
}
