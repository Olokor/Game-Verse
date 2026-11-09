using System.Globalization;
using GameVerse.Api.Endpoints;
using GameVerse.Api.Hosting;
using GameVerse.Infrastructure;
using Hangfire;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;

// Commands (same image for every role):
//   dotnet GameVerse.Api.dll              run, role from GameVerse__Role (api | worker)
//   dotnet GameVerse.Api.dll migrate      apply EF Core migrations and exit
//   dotnet GameVerse.Api.dll healthcheck  probe this container's /health (Docker HEALTHCHECK)
if (args.Length > 0 && args[0] == "healthcheck")
{
    return await HealthProbe.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection(GameVerseOptions.Section).Get<GameVerseOptions>() ?? new GameVerseOptions();
if (BuildTimeOpenApi.IsRunning)
{
    // `make schema` boots this host only to read endpoint metadata. Nothing connects, so placeholders are enough.
    options.ConnectionStrings = BuildTimeOpenApi.PlaceholderConnections();
}

options.Validate();
builder.Services.AddSingleton(options);

builder.Services.AddSerilog((services, logger) =>
{
    logger.ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("role", options.Role);
    if (options.Log.Pretty)
    {
        logger.WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);
    }
    else
    {
        logger.WriteTo.Console(new RenderedCompactJsonFormatter());
    }
});

builder.Services.AddGameVerseInfrastructure(options.ConnectionStrings, runJobServer: options.IsWorker);
builder.Services.AddOpenApi();

// TODO(gameverse): Sentry and OpenTelemetry (CLAUDE.md section 3) once there is real traffic (Phase 1).
var app = builder.Build();

if (args.Length > 0 && args[0] == "migrate")
{
    StartupLog.MigrationsStarting(app.Logger);
    await app.Services.MigrateDatabaseAsync();
    StartupLog.MigrationsApplied(app.Logger);
    return 0;
}

app.UseSerilogRequestLogging();
app.MapGameVerseHealth();

if (!options.IsWorker)
{
    app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
    app.MapEchoSocket();
    app.MapOpenApi();

    if (app.Environment.IsDevelopment())
    {
        app.MapScalarApiReference();
    }

    if (options.Hangfire.Dashboard)
    {
        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = [new HangfireDashboardAuth(app.Environment)],
            DisplayStorageConnectionString = false,
        });
    }
}

await app.RunAsync();
return 0;

/// <summary>Entry point. Public and partial so WebApplicationFactory&lt;Program&gt; can host it in integration tests.</summary>
public partial class Program;
