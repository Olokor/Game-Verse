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

        auth.MapPost("/upgrade", UpgradeAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Create)
            .Validate<UpgradeRequest>()
            .WithSummary("Turn the signed-in guest into an email account (same account id); mails a verification code")
            .Produces<AuthTokensResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        auth.MapPost("/email/verify", VerifyEmailAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.SignIn)
            .Validate<VerifyEmailRequest>()
            .WithSummary("Confirm the email of the signed-in account with the mailed 6-digit code")
            .Produces(StatusCodes.Status204NoContent);

        auth.MapPost("/email/resend", ResendAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Email)
            .WithSummary("Mail a new verification code")
            .Produces(StatusCodes.Status202Accepted);

        auth.MapPost("/login", LoginAsync)
            .RequireRateLimiting(RateLimits.SignIn)
            .Validate<LoginRequest>()
            .WithSummary("Sign in with email and password")
            .Produces<AuthTokensResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        auth.MapPost("/password/forgot", ForgotAsync)
            .RequireRateLimiting(RateLimits.Email)
            .Validate<ForgotPasswordRequest>()
            .WithSummary("Mail a password-reset code (always 202, whether or not the email has an account)")
            .Produces(StatusCodes.Status202Accepted);

        auth.MapPost("/password/reset", ResetAsync)
            .RequireRateLimiting(RateLimits.SignIn)
            .Validate<ResetPasswordRequest>()
            .WithSummary("Set a new password with the mailed code; signs out every session")
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

    private static async Task<IResult> UpgradeAsync(UpgradeRequest request, HttpContext http, EmailAccountService accounts)
    {
        if (AccountClaims.UserId(http.User) is not { } userId)
        {
            return InvalidCredentials();
        }

        var (outcome, tokens, errors) = await accounts.UpgradeAsync(userId, request.Email, request.Password, request.BirthYear, http.RequestAborted).ConfigureAwait(false);
        return outcome switch
        {
            UpgradeOutcome.Upgraded => Results.Ok(TokenDelivery.Deliver(http, tokens!, new AuthTokensResponse())),
            UpgradeOutcome.NotAGuest => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Already an email account"),
            UpgradeOutcome.EmailTaken => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Email already in use", detail: "Sign in with that email, or use another one."),
            UpgradeOutcome.InvalidPassword => Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(UpgradeRequest.Password)] = errors.ToArray() }),
            _ => InvalidCredentials(),
        };
    }

    private static async Task<IResult> VerifyEmailAsync(VerifyEmailRequest request, HttpContext http, EmailAccountService accounts)
    {
        if (AccountClaims.UserId(http.User) is not { } userId)
        {
            return InvalidCredentials();
        }

        return CodeResult(await accounts.VerifyEmailAsync(userId, request.Code, http.RequestAborted).ConfigureAwait(false), nameof(VerifyEmailRequest.Code));
    }

    private static async Task<IResult> ResendAsync(HttpContext http, EmailAccountService accounts)
    {
        if (AccountClaims.UserId(http.User) is { } userId)
        {
            await accounts.ResendVerificationAsync(userId, http.RequestAborted).ConfigureAwait(false);
        }

        return Results.Accepted();
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, HttpContext http, EmailAccountService accounts)
    {
        var tokens = await accounts.LoginAsync(request.Email, request.Password, http.RequestAborted).ConfigureAwait(false);
        return tokens is null
            ? Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Sign-in failed",
                detail: "The email or password is not valid, or the account is locked for 15 minutes after too many attempts.")
            : Results.Ok(TokenDelivery.Deliver(http, tokens, new AuthTokensResponse()));
    }

    private static async Task<IResult> ForgotAsync(ForgotPasswordRequest request, HttpContext http, EmailAccountService accounts)
    {
        await accounts.ForgotPasswordAsync(request.Email, http.RequestAborted).ConfigureAwait(false);
        return Results.Accepted();
    }

    private static async Task<IResult> ResetAsync(ResetPasswordRequest request, HttpContext http, EmailAccountService accounts)
    {
        var (check, errors) = await accounts.ResetPasswordAsync(request.Email, request.Code, request.NewPassword, http.RequestAborted).ConfigureAwait(false);
        var passwordErrors = errors.ToArray();
        if (passwordErrors.Length > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(ResetPasswordRequest.NewPassword)] = passwordErrors });
        }

        return CodeResult(check, nameof(ResetPasswordRequest.Code));
    }

    /// <summary>A wrong, expired or exhausted code is a 400 on the code field; the message says what to do next.</summary>
    private static IResult CodeResult(CodeCheck check, string field) => check switch
    {
        CodeCheck.Valid => Results.NoContent(),
        CodeCheck.Expired => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = ["The code has expired. Ask for a new one."] }),
        CodeCheck.TooManyAttempts => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = ["Too many wrong attempts. Ask for a new code."] }),
        _ => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = ["The code is not valid."] }),
    };

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

internal sealed class UpgradeRequestValidator : AbstractValidator<UpgradeRequest>
{
    public UpgradeRequestValidator(TimeProvider time)
    {
        var year = time.GetUtcNow().Year;
        RuleFor(r => r.Email).NotEmpty().MaximumLength(254).EmailAddress();
        RuleFor(r => r.Password).NotEmpty().MinimumLength(10).MaximumLength(128);
        RuleFor(r => r.BirthYear).InclusiveBetween(year - 100, year - 5).WithMessage("Enter the year you were born.");
    }
}

internal sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(254);
        RuleFor(r => r.Password).NotEmpty().MaximumLength(128);
    }
}

internal sealed class VerifyEmailRequestValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailRequestValidator() => RuleFor(r => r.Code).Matches("^[0-9]{6}$").WithMessage("Enter the 6-digit code.");
}

internal sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() => RuleFor(r => r.Email).NotEmpty().MaximumLength(254).EmailAddress();
}

internal sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(254).EmailAddress();
        RuleFor(r => r.Code).Matches("^[0-9]{6}$").WithMessage("Enter the 6-digit code.");
        RuleFor(r => r.NewPassword).NotEmpty().MinimumLength(10).MaximumLength(128);
    }
}
