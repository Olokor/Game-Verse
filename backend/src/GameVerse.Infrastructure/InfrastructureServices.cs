using GameVerse.Infrastructure.Persistence;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace GameVerse.Infrastructure;

/// <summary>Connection settings the infrastructure needs. Bound from the GameVerse:ConnectionStrings section.</summary>
public sealed class ConnectionStringsOptions
{
    public string Postgres { get; set; } = string.Empty;

    public string Redis { get; set; } = string.Empty;
}

/// <summary>
/// Registers EF Core, Redis and Hangfire. (.NET note: these "AddX" extension methods on IServiceCollection
/// are the standard way to package DI registrations, roughly a Spring @Configuration class.)
/// </summary>
public static class InfrastructureServices
{
    public const string HangfireSchema = "hangfire";

    public static IServiceCollection AddGameVerseInfrastructure(this IServiceCollection services, ConnectionStringsOptions connections, bool runJobServer)
    {
        ArgumentNullException.ThrowIfNull(connections);
        if (string.IsNullOrWhiteSpace(connections.Postgres) || string.IsNullOrWhiteSpace(connections.Redis))
        {
            throw new InvalidOperationException(
                "Missing connection strings. Set GameVerse__ConnectionStrings__Postgres and GameVerse__ConnectionStrings__Redis (see .env.example).");
        }

        services.AddDbContext<GameVerseDbContext>(options => ConfigureNpgsql(options, connections.Postgres));

        // One multiplexer per process, as StackExchange.Redis recommends. Connects lazily and retries.
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var config = ConfigurationOptions.Parse(connections.Redis);
            config.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(config);
        });

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                storage => storage.UseNpgsqlConnection(connections.Postgres),
                new PostgreSqlStorageOptions { SchemaName = HangfireSchema, PrepareSchemaIfNecessary = true }));

        if (runJobServer)
        {
            services.AddHangfireServer();
        }

        services.AddHealthChecks()
            .AddNpgSql(connections.Postgres, name: "postgres", timeout: TimeSpan.FromSeconds(3))
            .AddRedis(connections.Redis, name: "redis", timeout: TimeSpan.FromSeconds(3));

        return services;
    }

    /// <summary>Applies pending EF Core migrations. Used by the API's `migrate` command and by tests.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameVerseDbContext>();
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static void ConfigureNpgsql(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(GameVerseDbContext).Assembly.GetName().Name))
            .UseSnakeCaseNamingConvention();
}
