using System.Threading.RateLimiting;
using FluentValidation;
using GameVerse.Contracts.Catalog;
using GameVerse.Contracts.Realtime;
using GameVerse.Contracts.Session;
using GameVerse.Platform.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using static GameVerse.Platform.PlatformHttp;

namespace GameVerse.Platform.Realtime;

/// <summary>Registers the Realtime module and maps <c>/realtime/tickets</c> and <c>/ws/play/{sessionId}</c> (ADR-0009).</summary>
public static class RealtimeModule
{
    public static IServiceCollection AddRealtimeModule(this IServiceCollection services, RealtimeOptions options)
    {
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<RealtimeTickets>();
        services.AddSingleton<IRealtimeTickets>(sp => sp.GetRequiredService<RealtimeTickets>());
        services.AddSingleton<MatchHostRegistry>();
        services.AddHostedService(sp => sp.GetRequiredService<MatchHostRegistry>());
        services.AddTransient<MatchWatchdog>();
        services.AddSingleton<IRoomBroadcaster, RoomBroadcaster>();
        return services;
    }

    /// <summary>Ticket requests per client IP per minute: every (re)connect needs one.</summary>
    public static void AddRateLimitPolicies(RateLimiterOptions limiter, int ticketsPerMinute = 60)
    {
        ArgumentNullException.ThrowIfNull(limiter);
        limiter.AddPolicy(RateLimits.RealtimeTicket, http => RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = ticketsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    }

    public static IEndpointRouteBuilder MapRealtime(this IEndpointRouteBuilder app)
    {
        app.MapPost("/realtime/tickets", TicketAsync)
            .WithTags("Realtime")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.RealtimeTicket)
            .Validate<TicketRequest>()
            .WithSummary("A single-use WebSocket ticket (30 seconds): for a session you are seated in, or a private room you are in")
            .Produces<RealtimeInfo>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.Map("/ws/play/{sessionId:guid}", PlayAsync).ExcludeFromDescription();
        app.Map("/ws/room/{code}", RoomAsync).ExcludeFromDescription();
        return app;
    }

    /// <summary>The socket URL and a fresh ticket for a seated player, or null when the session is not played online.</summary>
    public static async Task<RealtimeInfo?> TicketForSessionAsync(IRealtimeTickets tickets, LiveSession session, Guid userId, HttpContext http, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tickets);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(http);
        if (!IsHosted(session.ModeKind) || session.SeatOf(userId) is null)
        {
            return null;
        }

        return await tickets.IssueAsync(TicketKind.Play, session.Id.ToString(), userId, RequestBase(http), ct).ConfigureAwait(false);
    }

    /// <summary>Modes the server's match host plays (ADR-0008): everything except what runs on the device.</summary>
    public static bool IsHosted(string modeKind) => modeKind is not (ModeKinds.Local or ModeKinds.Solo);

    private static async Task<IResult> TicketAsync(TicketRequest request, HttpContext http, IRealtimeTickets tickets, ISessionService sessions, IRoomFeed rooms)
    {
        if (SignedInUser(http) is not { } userId)
        {
            return InvalidCredentials();
        }

        if (request.RoomCode is { } code)
        {
            var normalized = code.ToUpperInvariant();
            return await rooms.GetForMemberAsync(normalized, userId, http.RequestAborted).ConfigureAwait(false) is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Room not found")
                : Results.Ok(await tickets.IssueAsync(TicketKind.Room, normalized, userId, RequestBase(http), http.RequestAborted).ConfigureAwait(false));
        }

        var live = Guid.TryParse(request.SessionId, out var sessionId)
            ? await sessions.GetLiveAsync(sessionId, http.RequestAborted).ConfigureAwait(false)
            : null;
        var info = live is null ? null : await TicketForSessionAsync(tickets, live, userId, http, http.RequestAborted).ConfigureAwait(false);

        // Unknown, not seated, or played on the device: the same 404, so session ids are not discoverable.
        return info is null ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Session not found") : Results.Ok(info);
    }

    private static async Task PlayAsync(
        HttpContext http, Guid sessionId, RealtimeTickets tickets, ISessionService sessions, MatchHostRegistry registry,
        IConnectionMultiplexer redis, RealtimeOptions options, TimeProvider time, ILoggerFactory loggers)
    {
        if (!http.WebSockets.IsWebSocketRequest)
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            await http.Response.WriteAsync("WebSocket connection expected.").ConfigureAwait(false);
            return;
        }

        // The ticket is redeemed (deleted) before anything else, so a failed attempt still uses it up.
        var claim = await tickets.RedeemAsync(http.Request.Query["ticket"]).ConfigureAwait(false);
        if (claim is null || claim.Kind != TicketKind.Play || claim.Target != sessionId.ToString())
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var live = await sessions.GetLiveAsync(sessionId, http.RequestAborted).ConfigureAwait(false);
        if (live is null || !IsHosted(live.ModeKind) || live.SeatOf(claim.UserId) is not { } seat)
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        using var socket = await http.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        var connection = new PlayConnection(socket, sessionId, seat, registry, redis, options, time, loggers.CreateLogger<PlayConnection>());
        await connection.RunAsync(http.RequestAborted).ConfigureAwait(false);
    }

    private static async Task RoomAsync(
        HttpContext http, string code, RealtimeTickets tickets, IRoomFeed rooms, IConnectionMultiplexer redis, RealtimeOptions options, TimeProvider time, ILoggerFactory loggers)
    {
        if (!http.WebSockets.IsWebSocketRequest)
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            await http.Response.WriteAsync("WebSocket connection expected.").ConfigureAwait(false);
            return;
        }

        var normalized = code.ToUpperInvariant();
        var claim = await tickets.RedeemAsync(http.Request.Query["ticket"]).ConfigureAwait(false);
        if (claim is null || claim.Kind != TicketKind.Room || claim.Target != normalized)
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (await rooms.GetForMemberAsync(normalized, claim.UserId, http.RequestAborted).ConfigureAwait(false) is not { } room)
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        using var socket = await http.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        await new RoomConnection(socket, normalized, room, redis, options, time, loggers.CreateLogger<RoomConnection>())
            .RunAsync(http.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>The scheme and host the client called, for building socket URLs (GameVerse:Realtime:PublicBaseUrl overrides it).</summary>
    public static Uri RequestBase(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return new(http.Request.Scheme + "://" + http.Request.Host.Value);
    }

    private static Guid? SignedInUser(HttpContext http) => Guid.TryParse(http.User.FindFirst("sub")?.Value, out var id) ? id : null;
}

internal sealed class TicketRequestValidator : AbstractValidator<TicketRequest>
{
    public TicketRequestValidator()
    {
        RuleFor(r => r).Must(r => (r.SessionId is null) != (r.RoomCode is null)).WithMessage("Set exactly one of sessionId and roomCode.");
        RuleFor(r => r.SessionId).MaximumLength(40);
        RuleFor(r => r.RoomCode).MaximumLength(12);
    }
}
