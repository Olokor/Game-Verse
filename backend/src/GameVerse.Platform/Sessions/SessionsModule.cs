using FluentValidation;
using GameVerse.Contracts.Catalog;
using GameVerse.Contracts.Session;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static GameVerse.Platform.PlatformHttp;

namespace GameVerse.Platform.Sessions;

/// <summary>Registers the Sessions module and maps <c>/sessions</c>.</summary>
public static class SessionsModule
{
    /// <summary>Modes a player can start directly. Online sessions come from rooms and matchmaking (1.4, 1.5).</summary>
    private static readonly HashSet<string> OfflineKinds = [ModeKinds.Local, ModeKinds.VsAi, ModeKinds.Solo];

    public static IServiceCollection AddSessionsModule(this IServiceCollection services)
    {
        services.AddScoped<SessionService>();
        services.AddScoped<ISessionService>(sp => sp.GetRequiredService<SessionService>());
        return services;
    }

    public static IEndpointRouteBuilder MapSessions(this IEndpointRouteBuilder app)
    {
        var sessions = app.MapGroup("/sessions").WithTags("Sessions").RequireAuthorization();

        sessions.MapPost("/", CreateAsync)
            .Validate<CreateSessionRequest>()
            .WithSummary("Start a single-owner session (pass and play, vs AI, solo); returns its manifest")
            .Produces<SessionManifest<SessionConfig<Dictionary<string, string>>, object?>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        sessions.MapGet("/{sessionId:guid}/manifest", ManifestAsync)
            .WithSummary("The SessionManifest a game loads with (participants only)")
            .Produces<SessionManifest<SessionConfig<Dictionary<string, string>>, object?>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        sessions.MapGet("/{sessionId:guid}/fairness", FairnessAsync)
            .WithSummary("Dice commit-reveal: the seed commitment always, the secret and seed once the session has ended (participants only)")
            .Produces<SessionFairness>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        sessions.MapGet("/me", MineAsync)
            .WithSummary("The signed-in player's recent sessions, optionally filtered by status")
            .Produces<List<SessionSummary>>();

        return app;
    }

    private static async Task<IResult> CreateAsync(CreateSessionRequest request, HttpContext http, SessionService sessions, Catalog.ICatalog catalog)
    {
        if (SignedInUser(http) is not { } userId)
        {
            return InvalidCredentials();
        }

        var game = await catalog.GetGameAsync(request.GameId, http.RequestAborted).ConfigureAwait(false);
        var mode = game?.Modes.SingleOrDefault(m => m.Key == request.Mode);
        if (mode is not null && !OfflineKinds.Contains(mode.Kind))
        {
            return Field(nameof(CreateSessionRequest.Mode), "Online modes start from a private room or matchmaking.");
        }

        if (game is not null && GameSetups.HouseRulesError(game.Id, request.HouseRules, request.PlayerCount) is { } rulesError)
        {
            return Field(nameof(CreateSessionRequest.HouseRules), rulesError);
        }

        var seats = new List<NewSeat> { new(userId, false, null) };
        for (var seat = 1; seat < request.PlayerCount; seat++)
        {
            seats.Add(mode?.Kind == ModeKinds.VsAi ? new NewSeat(null, true, request.AiLevel ?? 1) : new NewSeat(null, false, null));
        }

        var config = new SessionConfig<Dictionary<string, string>>
        {
            Variant = request.Variant,
            HouseRules = request.HouseRules,
            AiLevel = mode?.Kind == ModeKinds.VsAi ? request.AiLevel ?? 1 : null,
        };
        var (sessionId, error) = await sessions.CreateAsync(request.GameId, request.Mode, userId, seats, config, http.RequestAborted).ConfigureAwait(false);
        return error switch
        {
            CreateSessionError.None => Results.Created(
                $"/sessions/{sessionId}/manifest",
                await sessions.GetManifestAsync(sessionId!.Value, userId, http.RequestAborted).ConfigureAwait(false)),
            CreateSessionError.UnknownGame => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Game not found"),
            CreateSessionError.NotPlayable => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Coming soon", detail: "This game is not playable yet."),
            CreateSessionError.UnknownMode => Field(nameof(CreateSessionRequest.Mode), "This game has no such mode."),
            CreateSessionError.BadHouseRules => Field(nameof(CreateSessionRequest.HouseRules), "These house rules are not valid for this game."),
            _ => Field(nameof(CreateSessionRequest.PlayerCount), "This mode does not support that many players."),
        };
    }

    private static async Task<IResult> ManifestAsync(Guid sessionId, HttpContext http, SessionService sessions)
    {
        var manifest = SignedInUser(http) is { } userId
            ? await sessions.GetManifestAsync(sessionId, userId, http.RequestAborted).ConfigureAwait(false)
            : null;

        // Non-participants get the same 404 as a missing session: session ids are not discoverable.
        return manifest is null ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Session not found") : Results.Ok(manifest);
    }

    private static async Task<IResult> FairnessAsync(Guid sessionId, HttpContext http, SessionService sessions)
    {
        var fairness = SignedInUser(http) is { } userId
            ? await sessions.GetFairnessAsync(sessionId, userId, http.RequestAborted).ConfigureAwait(false)
            : null;
        return fairness is null ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Session not found") : Results.Ok(fairness);
    }

    private static async Task<IResult> MineAsync(HttpContext http, SessionService sessions, string? status = null) =>
        SignedInUser(http) is { } userId
            ? Results.Ok(await sessions.ListForUserAsync(userId, status, http.RequestAborted).ConfigureAwait(false))
            : InvalidCredentials();

    private static IResult Field(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static Guid? SignedInUser(HttpContext http) => Guid.TryParse(http.User.FindFirst("sub")?.Value, out var id) ? id : null;
}

internal sealed class CreateSessionRequestValidator : AbstractValidator<CreateSessionRequest>
{
    public CreateSessionRequestValidator()
    {
        RuleFor(r => r.GameId).NotEmpty().MaximumLength(40);
        RuleFor(r => r.Mode).NotEmpty().MaximumLength(40);
        RuleFor(r => r.PlayerCount).InclusiveBetween(1, 8);
        RuleFor(r => r.AiLevel).InclusiveBetween(1, 3).When(r => r.AiLevel is not null);
        RuleFor(r => r.Variant).MaximumLength(40);
        RuleFor(r => r.HouseRules).Must(rules => rules.Count <= 32 && rules.All(kv => kv.Key.Length <= 40 && kv.Value.Length <= 40))
            .WithMessage("Too many or too long house rules.");
    }
}
