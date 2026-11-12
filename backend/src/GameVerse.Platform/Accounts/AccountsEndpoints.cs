using System.Globalization;
using FluentValidation;
using GameVerse.Contracts.Accounts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using static GameVerse.Platform.PlatformHttp;

namespace GameVerse.Platform.Accounts;

/// <summary>Auth and account endpoints (Minimal API group, CLAUDE.md section 3). Wire types live in GameVerse.Contracts.</summary>
public static class AccountsEndpoints
{
    public static IEndpointRouteBuilder MapAccounts(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/auth").WithTags("Auth");

        auth.MapPost("/guest", CreateGuestAsync)
            .RequireRateLimiting(RateLimits.Create)
            .WithSummary("Create a device-bound guest account")
            .Produces<GuestCreatedResponse>(StatusCodes.Status201Created);

        auth.MapPost("/guest/login", GuestLoginAsync)
            .RequireRateLimiting(RateLimits.SignIn)
            .Validate<GuestLoginRequest>()
            .WithSummary("Sign a guest back in with its device secret")
            .Produces<AuthTokensResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        auth.MapPost("/refresh", RefreshAsync)
            .RequireRateLimiting(RateLimits.SignIn)
            .WithSummary("Exchange a refresh token for new tokens (rotates the refresh token)")
            .Produces<AuthTokensResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        auth.MapPost("/logout", LogoutAsync)
            .WithSummary("End the session the refresh token belongs to")
            .Produces(StatusCodes.Status204NoContent);

        app.MapGet("/accounts/me", MeAsync)
            .RequireAuthorization()
            .WithTags("Accounts")
            .WithSummary("The signed-in account")
            .Produces<AccountResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> CreateGuestAsync(HttpContext http, AuthService auth)
    {
        var (tokens, secret) = await auth.CreateGuestAsync(http.RequestAborted).ConfigureAwait(false);
        var body = TokenDelivery.Deliver(http, tokens, new GuestCreatedResponse { DeviceSecret = secret });
        return Results.Created("/accounts/me", body);
    }

    private static async Task<IResult> GuestLoginAsync(GuestLoginRequest request, HttpContext http, AuthService auth)
    {
        var tokens = await auth.GuestLoginAsync(Guid.Parse(request.GuestId), request.DeviceSecret, http.RequestAborted).ConfigureAwait(false);
        return tokens is null ? InvalidCredentials() : Results.Ok(TokenDelivery.Deliver(http, tokens, new AuthTokensResponse()));
    }

    private static async Task<IResult> RefreshAsync(HttpContext http, AuthService auth, RefreshRequest? request = null)
    {
        var presented = TokenDelivery.ReadRefreshToken(http, request);
        var tokens = presented is null ? null : await auth.RefreshAsync(presented, http.RequestAborted).ConfigureAwait(false);
        if (tokens is null)
        {
            TokenDelivery.ClearCookie(http);
            return InvalidCredentials();
        }

        return Results.Ok(TokenDelivery.Deliver(http, tokens, new AuthTokensResponse()));
    }

    private static async Task<IResult> LogoutAsync(HttpContext http, AuthService auth, RefreshRequest? request = null)
    {
        var presented = TokenDelivery.ReadRefreshToken(http, request);
        if (presented is not null)
        {
            await auth.LogoutAsync(presented, http.RequestAborted).ConfigureAwait(false);
        }

        TokenDelivery.ClearCookie(http);
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(HttpContext http, IAccountDirectory directory)
    {
        var summary = AccountClaims.UserId(http.User) is { } id
            ? await directory.GetSummaryAsync(id, http.RequestAborted).ConfigureAwait(false)
            : null;
        if (summary is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Account not found");
        }

        return Results.Ok(new AccountResponse
        {
            UserId = summary.UserId.ToString(),
            IsGuest = summary.IsGuest,
            Email = summary.Email,
            EmailVerified = summary.EmailVerified,
            IsMinor = summary.IsMinor,
            CreatedAt = summary.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
        });
    }
}

/// <summary>
/// Puts tokens in the response. Web clients (<c>X-GameVerse-Client: web</c>) get the refresh token as an httpOnly,
/// Secure, SameSite=Strict cookie scoped to /auth, so page scripts can never read it; everyone else gets it in the body.
/// </summary>
internal static class TokenDelivery
{
    public static T Deliver<T>(HttpContext http, IssuedTokens tokens, T body)
        where T : AuthTokensResponse
    {
        body.UserId = tokens.User.Id.ToString();
        body.IsGuest = tokens.User.IsGuest;
        body.AccessToken = tokens.AccessToken;
        body.AccessTokenExpiresAt = tokens.AccessTokenExpiresAt.ToString("O", CultureInfo.InvariantCulture);
        body.RefreshTokenExpiresAt = tokens.RefreshTokenExpiresAt.ToString("O", CultureInfo.InvariantCulture);
        if (IsWeb(http))
        {
            http.Response.Cookies.Append(AuthHeaders.RefreshCookie, tokens.RefreshToken, CookieOptions(tokens.RefreshTokenExpiresAt));
            body.RefreshToken = null;
        }
        else
        {
            body.RefreshToken = tokens.RefreshToken;
        }

        return body;
    }

    public static string? ReadRefreshToken(HttpContext http, RefreshRequest? request)
    {
        var fromBody = request?.RefreshToken;
        if (!string.IsNullOrWhiteSpace(fromBody))
        {
            return fromBody;
        }

        return http.Request.Cookies.TryGetValue(AuthHeaders.RefreshCookie, out var cookie) && !string.IsNullOrWhiteSpace(cookie) ? cookie : null;
    }

    public static void ClearCookie(HttpContext http)
    {
        if (IsWeb(http))
        {
            http.Response.Cookies.Delete(AuthHeaders.RefreshCookie, CookieOptions(null));
        }
    }

    private static bool IsWeb(HttpContext http) =>
        string.Equals(http.Request.Headers[AuthHeaders.Client], AuthHeaders.WebClient, StringComparison.OrdinalIgnoreCase);

    private static CookieOptions CookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/auth",
        Expires = expires,
        IsEssential = true,
    };
}

internal sealed class GuestLoginRequestValidator : AbstractValidator<GuestLoginRequest>
{
    public GuestLoginRequestValidator()
    {
        RuleFor(r => r.GuestId).Must(id => Guid.TryParse(id, out _)).WithMessage("Must be a valid account id.");
        RuleFor(r => r.DeviceSecret).NotEmpty().MaximumLength(128);
    }
}
