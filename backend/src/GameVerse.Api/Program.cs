using System.Globalization;
using GameVerse.Api.Endpoints;
using GameVerse.Api.Hosting;
using GameVerse.Infrastructure;
using GameVerse.Infrastructure.Email;
using GameVerse.Platform;
using GameVerse.Platform.Accounts;
using GameVerse.Platform.Catalog;
using GameVerse.Platform.Profiles;
using GameVerse.Platform.Sessions;
using Hangfire;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
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

    if (options.ErrorReporting.IsEnabled)
    {
        ErrorReporting.AddSerilogSink(logger);
    }
});

// Error reporting to self-hosted GlitchTip (ADR-0005). Off unless GameVerse:ErrorReporting:Dsn is set.
if (options.ErrorReporting.IsEnabled)
{
    builder.WebHost.UseSentry(sentry => ErrorReporting.ConfigureSdk(sentry, options.ErrorReporting));
}

// The web app calls the API from its own origin with credentials (refresh cookie, ADR-0006).
builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy
    .WithOrigins(options.CorsOrigins)
    .AllowCredentials()
    .WithHeaders("Authorization", "Content-Type", GameVerse.Contracts.Accounts.AuthHeaders.Client)
    .WithMethods("GET", "POST", "PATCH", "DELETE")));

// Unhandled exceptions become RFC 7807 problem+json with a traceId; the body never carries exception details.
builder.Services.AddProblemDetails();

builder.Services.AddGameVerseInfrastructure(options.ConnectionStrings, runJobServer: options.IsWorker);

// Accounts (Prompt 1.1, ADR-0006). The signing key is required outside Development; `make schema` boots without one.
var accountsOptions = builder.Configuration.GetSection(AccountsOptions.Section).Get<AccountsOptions>() ?? new AccountsOptions();
var signingKey = AccountsSigningKey.Create(accountsOptions.SigningKey, allowEphemeral: builder.Environment.IsDevelopment() || BuildTimeOpenApi.IsRunning);
builder.Services.AddAccountsModule(accountsOptions, signingKey).AddGameVerseIdentityStores();
builder.Services.AddProfilesModule();
builder.Services.AddCatalogModule();
builder.Services.AddSessionsModule();
builder.Services.AddPlatformValidators();
builder.Services.AddGameVerseEmail(builder.Configuration.GetSection(EmailOptions.Section).Get<EmailOptions>() ?? new EmailOptions());
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.OnRejected = async (context, ct) =>
    {
        var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails = { Status = StatusCodes.Status429TooManyRequests, Title = "Too many requests", Detail = "Please wait a moment and try again." },
        });
    };
    AccountsModule.AddRateLimitPolicies(limiter, accountsOptions.RateLimits);
});
builder.Services.AddOpenApi(openApi => openApi.AddSchemaTransformer<OpenApiRequiredProperties>());

// TODO(gameverse): OpenTelemetry tracing (CLAUDE.md section 3) once there is real traffic (Phase 1). Errors: ADR-0005.
var app = builder.Build();

if (args.Length > 0 && args[0] == "migrate")
{
    StartupLog.MigrationsStarting(app.Logger);
    await app.Services.MigrateDatabaseAsync();
    StartupLog.MigrationsApplied(app.Logger);
    return 0;
}

if (signingKey.IsEphemeral && !BuildTimeOpenApi.IsRunning)
{
    StartupLog.EphemeralSigningKey(app.Logger);
}

app.UseSerilogRequestLogging(requestLog =>
{
    // A 500's exception is already logged (and reported) at Error by the exception handler; logging the access line at
    // Error too would report every failure twice. Access lines: Warning for 5xx, Information otherwise.
    requestLog.GetLevel = (http, _, exception) =>
        exception is not null || http.Response.StatusCode >= 500 ? LogEventLevel.Warning : LogEventLevel.Information;
});
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapGameVerseHealth();

if (!options.IsWorker)
{
    app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
    app.MapEchoSocket();
    app.MapAccounts();
    app.MapProfiles();
    app.MapCatalog();
    app.MapSessions();
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
