using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
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

    // Mail catcher with an HTTP API: tests read the codes the API mails (same image as docker-compose.yml).
    private readonly IContainer mailpit = new ContainerBuilder("axllent/mailpit:v1.32.0")
        .WithPortBinding(1025, true)
        .WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/api/v1/info")))
        .Build();

    public string PostgresConnectionString => postgres.GetConnectionString();

    public string RedisConnectionString => redis.GetConnectionString();

    public string MailpitHost => mailpit.Hostname;

    public int MailpitSmtpPort => mailpit.GetMappedPublicPort(1025);

    public Uri MailpitApi => new($"http://{mailpit.Hostname}:{mailpit.GetMappedPublicPort(8025)}/");

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(postgres.StartAsync(), redis.StartAsync(), mailpit.StartAsync());

        // Migrate once for the whole assembly. Tests that each migrated in parallel raced on Postgres's catalog
        // ("duplicate key value violates unique constraint pg_namespace_nspname_index").
        await using var factory = new GameVerseApiFactory(this);
        await factory.Services.MigrateDatabaseAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await postgres.DisposeAsync();
        await redis.DisposeAsync();
        await mailpit.DisposeAsync();
    }
}
