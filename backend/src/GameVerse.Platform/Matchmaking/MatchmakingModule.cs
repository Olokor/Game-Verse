using System.Threading.RateLimiting;
using FluentValidation;
using GameVerse.Contracts.Catalog;
using GameVerse.Contracts.Matchmaking;
using GameVerse.Platform.Catalog;
using GameVerse.Platform.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static GameVerse.Platform.PlatformHttp;

namespace GameVerse.Platform.Matchmaking;

/// <summary>Registers public matchmaking and ratings, and maps <c>/matchmaking</c> (CLAUDE.md 4.6, ADR-0010).</summary>
public static class MatchmakingModule
{
    public static IServiceCollection AddMatchmakingModule(this IServiceCollection services, MatchmakingOptions options, bool runMatcher)
    {
        services.AddSingleton(options);
        services.AddSingleton<QueueStore>();
        services.AddScoped<RatingService>();
        services.AddScoped<IMatchFinishedHandler, RatingsOnFinish>();
        services.AddSingleton<MatchmakerService>();
        if (runMatcher)
        {
            services.AddHostedService(sp => sp.GetRequiredService<MatchmakerService>());
        }

        return services;
    }

    /// <summary>Queue joins per client IP per minute (CLAUDE.md section 7).</summary>
    public static void AddRateLimitPolicies(RateLimiterOptions limiter, int perMinute = 30)
    {
        ArgumentNullException.ThrowIfNull(limiter);
        limiter.AddPolicy(RateLimits.Matchmaking, http => RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    }

    public static IEndpointRouteBuilder MapMatchmaking(this IEndpointRouteBuilder app)
    {
        var matchmaking = app.MapGroup("/matchmaking").WithTags("Matchmaking").RequireAuthorization();

        matchmaking.MapPost("/queue", JoinAsync)
            .RequireRateLimiting(RateLimits.Matchmaking)
            .Validate<JoinQueueRequest>()
            .WithSummary("Start searching for a casual or ranked game; poll GET /matchmaking/queue until matched")
            .Produces<QueueStatusDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        matchmaking.MapGet("/queue", StatusAsync)
            .WithSummary("Searching (with the wait and the current rating band) or matched (with the session to load); 404 when not searching")
            .Produces<QueueStatusDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        matchmaking.MapDelete("/queue", CancelAsync)
            .WithSummary("Stop searching")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        matchmaking.MapGet("/ratings/{gameId}", RatingAsync)
            .WithSummary("Your Glicko-2 rating in a game (1500 and provisional until you play ranked)")
            .Produces<RatingDto>();

        return app;
    }

    private static async Task<IResult> JoinAsync(
        JoinQueueRequest request, HttpContext http, ICatalog catalog, QueueStore store, RatingService ratings, TimeProvider time)
    {
        if (SignedInUser(http) is not { } userId)
        {
            return InvalidCredentials();
        }

        var game = await catalog.GetGameAsync(request.GameId, http.RequestAborted).ConfigureAwait(false);
        if (game is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Game not found");
        }

        if (game.Status != GameStatuses.Enabled)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Coming soon", detail: "This game is not playable yet.");
        }

        var mode = game.Modes.SingleOrDefault(m => m.Key == request.ModeKey);
        if (mode is null || mode.Kind is not (ModeKinds.OnlineCasual or ModeKinds.OnlineRanked))
        {
            return Field(nameof(JoinQueueRequest.ModeKey), "Matchmaking is for casual and ranked modes; private rooms use a code.");
        }

        if (!mode.PlayerCounts.Contains(request.PlayerCount))
        {
            return Field(nameof(JoinQueueRequest.PlayerCount), "This mode does not support that many players.");
        }

        var now = time.GetUtcNow();
        var rating = await ratings.GetAsync(userId, game.Id, http.RequestAborted).ConfigureAwait(false);
        var entry = new QueueEntry
        {
            UserId = userId,
            Queue = QueueStore.QueueKey(game.Id, mode.Key, request.PlayerCount),
            GameId = game.Id,
            ModeKey = mode.Key,
            ModeKind = mode.Kind,
            PlayerCount = request.PlayerCount,
            Rating = rating.Rating,
            JoinedAt = now.ToUnixTimeMilliseconds(),
        };
        if (!await store.JoinAsync(entry).ConfigureAwait(false))
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Already searching", detail: "Cancel the current search first.");
        }

        return Results.Accepted("/matchmaking/queue", Searching(entry, now, http.RequestServices.GetRequiredService<MatchmakingOptions>()));
    }

    private static async Task<IResult> StatusAsync(HttpContext http, QueueStore store, MatchmakingOptions options, TimeProvider time)
    {
        if (SignedInUser(http) is not { } userId)
        {
            return InvalidCredentials();
        }

        if (await store.GetEntryAsync(userId).ConfigureAwait(false) is { } entry)
        {
            return Results.Ok(Searching(entry, time.GetUtcNow(), options));
        }

        if (await store.GetMatchedAsync(userId).ConfigureAwait(false) is { } matched)
        {
            return Results.Ok(new QueueStatusDto
            {
                Status = QueueStatuses.Matched,
                GameId = matched.GameId,
                ModeKey = matched.ModeKey,
                PlayerCount = matched.PlayerCount,
                WaitedSeconds = matched.WaitedSeconds,
                SessionId = matched.SessionId.ToString(),
            });
        }

        return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not searching");
    }

    private static async Task<IResult> CancelAsync(HttpContext http, QueueStore store)
    {
        if (SignedInUser(http) is not { } userId)
        {
            return InvalidCredentials();
        }

        var entry = await store.GetEntryAsync(userId).ConfigureAwait(false);
        return entry is not null && await store.CancelAsync(entry).ConfigureAwait(false)
            ? Results.NoContent()
            : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not searching", detail: "No search to cancel; it may have just been matched.");
    }

    private static async Task<IResult> RatingAsync(string gameId, HttpContext http, RatingService ratings) =>
        SignedInUser(http) is { } userId
            ? Results.Ok(await ratings.GetDtoAsync(userId, gameId, http.RequestAborted).ConfigureAwait(false))
            : InvalidCredentials();

    private static QueueStatusDto Searching(QueueEntry entry, DateTimeOffset now, MatchmakingOptions options)
    {
        var waited = now - DateTimeOffset.FromUnixTimeMilliseconds(entry.JoinedAt);
        return new QueueStatusDto
        {
            Status = QueueStatuses.Searching,
            GameId = entry.GameId,
            ModeKey = entry.ModeKey,
            PlayerCount = entry.PlayerCount,
            WaitedSeconds = Math.Max(0, (int)waited.TotalSeconds),
            RatingBand = entry.ModeKind == ModeKinds.OnlineRanked ? options.Band(waited) : null,
        };
    }

    private static IResult Field(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static Guid? SignedInUser(HttpContext http) => Guid.TryParse(http.User.FindFirst("sub")?.Value, out var id) ? id : null;
}

internal sealed class JoinQueueRequestValidator : AbstractValidator<JoinQueueRequest>
{
    public JoinQueueRequestValidator()
    {
        RuleFor(r => r.GameId).NotEmpty().MaximumLength(40);
        RuleFor(r => r.ModeKey).NotEmpty().MaximumLength(40);
        RuleFor(r => r.PlayerCount).InclusiveBetween(2, 8);
    }
}
