namespace GameVerse.Platform.Accounts;

/// <summary>
/// Auth settings, bound from the <c>GameVerse:Auth</c> configuration section (environment variables
/// <c>GameVerse__Auth__*</c>). See ADR-0006.
/// </summary>
public sealed class AccountsOptions
{
    public const string Section = "GameVerse:Auth";

    /// <summary>
    /// ECDSA P-256 private key (PEM) that signs access tokens (ES256). Required outside Development; in Development an
    /// ephemeral key is generated when empty (tokens then stop working when the API restarts).
    /// </summary>
    public string? SigningKey { get; set; }

    public string Issuer { get; set; } = "gameverse";

    public string Audience { get; set; } = "gameverse-clients";

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// A refresh token presented again within this window after it was used is treated as a network retry (the
    /// client never received the response), not as theft: the family is rotated again instead of revoked.
    /// Mobile connections in our markets drop responses often enough that strict reuse detection would log
    /// players out.
    /// </summary>
    public TimeSpan RefreshRetryGrace { get; set; } = TimeSpan.FromSeconds(15);

    public AuthRateLimits RateLimits { get; set; } = new();
}

/// <summary>Per-IP limits for auth endpoints (CLAUDE.md section 7). Tests raise them where a flow needs more calls.</summary>
public sealed class AuthRateLimits
{
    /// <summary>Sign-in style calls (login, guest login, refresh, verify, reset) per IP per minute.</summary>
    public int SignInPerMinute { get; set; } = 10;

    /// <summary>Account creation calls (new guest, upgrade) per IP per minute.</summary>
    public int CreatePerMinute { get; set; } = 5;

    /// <summary>Calls that send email (resend code, forgot password) per IP per 15 minutes.</summary>
    public int EmailPer15Minutes { get; set; } = 3;
}
