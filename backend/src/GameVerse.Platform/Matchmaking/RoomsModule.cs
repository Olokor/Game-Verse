using System.Text.Json;
using System.Threading.RateLimiting;
using FluentValidation;
using GameVerse.Contracts.Matchmaking;
using GameVerse.Platform.Realtime;
using GameVerse.Platform.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static GameVerse.Platform.PlatformHttp;

namespace GameVerse.Platform.Matchmaking;

/// <summary>Registers private rooms and maps <c>/rooms</c> (CLAUDE.md 4.6, ADR-0009).</summary>
public static class RoomsModule
{
    public static IServiceCollection AddRoomsModule(this IServiceCollection services, RoomsOptions options)
    {
        services.AddSingleton(options);
        services.AddScoped<RoomService>();
        services.AddScoped<IRoomFeed, RoomFeed>();
        services.AddScoped<IMatchFinishedHandler, RoomMatchFinished>();
        services.AddTransient<RoomCleanup>();
        return services;
    }

    /// <summary>Room creation and joining per client IP per minute (CLAUDE.md section 7).</summary>
    public static void AddRateLimitPolicies(RateLimiterOptions limiter, int perMinute = 30)
    {
        ArgumentNullException.ThrowIfNull(limiter);
        limiter.AddPolicy(RateLimits.Rooms, http => RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    }

    public static IEndpointRouteBuilder MapRooms(this IEndpointRouteBuilder app)
    {
        var rooms = app.MapGroup("/rooms").WithTags("Rooms").RequireAuthorization();

        rooms.MapPost("/", CreateAsync)
            .RequireRateLimiting(RateLimits.Rooms)
            .Validate<CreateRoomRequest>()
            .WithSummary("Open a private room; you are the host on seat 0")
            .Produces<RoomDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound);

        rooms.MapGet("/{code}", GetAsync)
            .WithSummary("A room by code (404 unknown, 410 expired or closed)")
            .Produces<RoomDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status410Gone);

        rooms.MapPost("/{code}/join", (string code, HttpContext http, RoomService service) =>
                Run(http, code, (user, c) => service.JoinAsync(c, user, http.RequestAborted)))
            .RequireRateLimiting(RateLimits.Rooms)
            .WithSummary("Take the first free seat (lobby only)")
            .Produces<RoomDto>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone);

        rooms.MapPost("/{code}/ready", (string code, RoomReadyRequest request, HttpContext http, RoomService service) =>
                Run(http, code, (user, c) => service.SetReadyAsync(c, user, request.Ready, http.RequestAborted)))
            .WithSummary("Mark yourself ready or not. After a game, everyone ready starts the next game (or a new series)")
            .Produces<RoomDto>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        rooms.MapPatch("/{code}/rules", (string code, RoomRulesRequest request, HttpContext http, RoomService service) =>
                Run(http, code, (user, c) => service.SetRulesAsync(c, user, request.HouseRules, http.RequestAborted)))
            .Validate<RoomRulesRequest>()
            .WithSummary("Change the house rules (host, lobby only); everyone's ready flag resets")
            .Produces<RoomDto>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        rooms.MapPost("/{code}/leave", (string code, HttpContext http, RoomService service) =>
                Run(http, code, (user, c) => service.LeaveAsync(c, user, http.RequestAborted)))
            .WithSummary("Leave the room. During a game your seat frees when the game ends; resign or disconnect to forfeit it")
            .Produces<RoomDto>();

        rooms.MapPost("/{code}/start", (string code, HttpContext http, RoomService service) =>
                Run(http, code, (user, c) => service.StartAsync(c, user, http.RequestAborted)))
            .WithSummary("Start the first game (host; every seat filled and ready). The room then carries the session id")
            .Produces<RoomDto>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> CreateAsync(CreateRoomRequest request, HttpContext http, RoomService service)
    {
        if (SignedInUser(http) is not { } userId)
        {
            return InvalidCredentials();
        }

        var result = await service.CreateAsync(userId, request, http.RequestAborted).ConfigureAwait(false);
        return result.Room is { } room ? Results.Created($"/rooms/{room.Code}", room) : ToProblem(result);
    }

    private static async Task<IResult> GetAsync(string code, RoomService service, HttpContext http)
    {
        if (RoomCodes.Normalize(code) is not { } normalized)
        {
            return ToProblem(RoomResult.Fail(RoomError.NotFound));
        }

        var result = await service.GetAsync(normalized, http.RequestAborted).ConfigureAwait(false);
        return result.Room is { } room ? Results.Ok(room) : ToProblem(result);
    }

    private static async Task<IResult> Run(HttpContext http, string code, Func<Guid, string, Task<RoomResult>> action)
    {
        if (SignedInUser(http) is not { } userId)
        {
            return InvalidCredentials();
        }

        if (RoomCodes.Normalize(code) is not { } normalized)
        {
            return ToProblem(RoomResult.Fail(RoomError.NotFound));
        }

        var result = await action(userId, normalized).ConfigureAwait(false);
        return result.Room is { } room ? Results.Ok(room) : ToProblem(result);
    }

    private static IResult ToProblem(RoomResult result) => result.Error switch
    {
        RoomError.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Room not found"),
        RoomError.Gone => Results.Problem(statusCode: StatusCodes.Status410Gone, title: "Room closed", detail: "This room has expired or closed."),
        RoomError.NotHost => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Host only", detail: "Only the room's host can do that."),
        RoomError.NotSeated => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not in this room"),
        RoomError.Full => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Room full", detail: "Every seat is taken."),
        RoomError.WrongStatus => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Not now", detail: "The room is not in a state that allows this."),
        RoomError.NotReady => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Not ready", detail: "Every seat must be filled and ready."),
        _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["Room"] = [result.Reason ?? "These settings are not valid for this game."] }),
    };

    private static Guid? SignedInUser(HttpContext http) => Guid.TryParse(http.User.FindFirst("sub")?.Value, out var id) ? id : null;
}

/// <summary>Rooms as the Realtime lobby socket sees them.</summary>
internal sealed class RoomFeed(RoomService rooms) : IRoomFeed
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<JsonElement?> GetForMemberAsync(string code, Guid userId, CancellationToken ct = default) =>
        RoomCodes.Normalize(code) is { } normalized && await rooms.GetForMemberAsync(normalized, userId, ct).ConfigureAwait(false) is { } room
            ? JsonSerializer.SerializeToElement(room, Json)
            : null;
}

/// <summary>
/// Scores a room's series when one of its games ends. Resolves the room service on use: the session service depends on
/// this handler and the room service depends on the session service, so taking it in the constructor would be a cycle.
/// </summary>
internal sealed class RoomMatchFinished(IServiceProvider scope) : IMatchFinishedHandler
{
    public Task OnMatchFinishedAsync(Guid sessionId, MatchOutcome outcome, CancellationToken ct) =>
        scope.GetRequiredService<RoomService>().OnMatchFinishedAsync(sessionId, outcome, ct);
}

/// <summary>Daily Hangfire job on the worker: deletes rooms that closed or expired more than a day ago.</summary>
public sealed class RoomCleanup(IServiceProvider services)
{
    public const string JobId = "matchmaking-room-cleanup";

    public async Task<int> RunAsync()
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DbContext>();
        var before = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().AddDays(-1);
        return await db.Set<Room>()
            .Where(r => (r.Status == RoomStatuses.Closed && r.UpdatedAt < before) || r.ExpiresAt < before)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);
    }
}

internal sealed class CreateRoomRequestValidator : AbstractValidator<CreateRoomRequest>
{
    public CreateRoomRequestValidator()
    {
        RuleFor(r => r.GameId).NotEmpty().MaximumLength(40);
        RuleFor(r => r.PlayerCount).InclusiveBetween(2, 8);
        RuleFor(r => r.BestOf).Must(b => b is 1 or 3).WithMessage("Best of 1 or 3.");
        RuleFor(r => r.HouseRules).Must(HouseRulesSize.IsSmall).WithMessage("Too many or too long house rules.");
    }
}

internal sealed class RoomRulesRequestValidator : AbstractValidator<RoomRulesRequest>
{
    public RoomRulesRequestValidator() =>
        RuleFor(r => r.HouseRules).Must(HouseRulesSize.IsSmall).WithMessage("Too many or too long house rules.");
}

internal static class HouseRulesSize
{
    public static bool IsSmall(Dictionary<string, string> rules) => rules.Count <= 32 && rules.All(kv => kv.Key.Length <= 40 && kv.Value.Length <= 40);
}
