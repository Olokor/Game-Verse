using GameVerse.Infrastructure;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

[assembly: AssemblyFixture(typeof(GameVerse.Api.IntegrationTests.StackFixture))]

namespace GameVerse.Api.IntegrationTests;

/// <summary>
/// Real Postgres 16 and Redis 7 in Docker, started once for the whole test assembly (Testcontainers).
/// Same major versions as docker-compose.yml.
/// </summary>
public sealed class StackFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("gameverse")
        .Build();

    private readonly RedisContainer redis = new RedisBuilder("redis:7-alpine")
        .Build();

    public string PostgresConnectionString => postgres.GetConnectionString();

    public string RedisConnectionString => redis.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(postgres.StartAsync(), redis.StartAsync());

        // Migrate once for the whole assembly. Tests that each migrated in parallel raced on Postgres's catalog
        // ("duplicate key value violates unique constraint pg_namespace_nspname_index").
        await using var factory = new GameVerseApiFactory(this);
        await factory.Services.MigrateDatabaseAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await postgres.DisposeAsync();
        await redis.DisposeAsync();
    }
}
