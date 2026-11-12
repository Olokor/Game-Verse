using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GameVerse.Platform.Accounts;

/// <summary>
/// Registers the Accounts module: Identity, ES256 JWT bearer authentication, the auth services and validators.
/// The host adds the EF Core stores (Infrastructure) to the returned <see cref="IdentityBuilder"/>.
/// </summary>
public static class AccountsModule
{
    public static IdentityBuilder AddAccountsModule(this IServiceCollection services, AccountsOptions options, AccountsSigningKey signingKey)
    {
        ArgumentNullException.ThrowIfNull(options);
        services.AddSingleton(options);
        services.AddSingleton(signingKey);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<AccessTokenIssuer>();
        services.AddScoped<AuthService>();
        services.AddScoped<EmailCodes>();
        services.AddScoped<EmailAccountService>();
        services.AddScoped<IAccountDirectory, AccountDirectory>();
        services.AddValidatorsFromAssemblyContaining<AccountsOptions>(includeInternalTypes: true);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(jwt =>
        {
            jwt.MapInboundClaims = false; // keep "sub" as "sub"
            jwt.TokenValidationParameters = AccessTokenIssuer.ValidationParameters(signingKey, options);
        });
        services.AddAuthorization();

        return services.AddIdentityCore<AccountUser>(identity =>
            {
                // NIST 800-63B: length over composition rules.
                identity.Password.RequiredLength = 10;
                identity.Password.RequireDigit = false;
                identity.Password.RequireLowercase = false;
                identity.Password.RequireUppercase = false;
                identity.Password.RequireNonAlphanumeric = false;
                identity.Password.RequiredUniqueChars = 4;
                identity.Lockout.MaxFailedAccessAttempts = 5;
                identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                identity.Lockout.AllowedForNewUsers = true;
                // Guests have no email, so Identity cannot require one; upgrade checks uniqueness itself.
                identity.User.RequireUniqueEmail = false;
            })
            .AddRoles<IdentityRole<Guid>>();
    }

    /// <summary>Per-IP fixed-window limits for auth endpoints (CLAUDE.md section 7).</summary>
    public static void AddRateLimitPolicies(RateLimiterOptions limiter, AuthRateLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limiter);
        ArgumentNullException.ThrowIfNull(limits);
        Add(limiter, PlatformHttp.RateLimits.SignIn, limits.SignInPerMinute, TimeSpan.FromMinutes(1));
        Add(limiter, PlatformHttp.RateLimits.Create, limits.CreatePerMinute, TimeSpan.FromMinutes(1));
        Add(limiter, PlatformHttp.RateLimits.Email, limits.EmailPer15Minutes, TimeSpan.FromMinutes(15));
    }

    private static void Add(RateLimiterOptions limiter, string policy, int permits, TimeSpan window) =>
        limiter.AddPolicy(policy, http => RateLimitPartition.GetFixedWindowLimiter(
            // TODO(gameverse): behind Traefik + Cloudflare, take the client IP from CF-Connecting-IP / X-Forwarded-For
            // of known proxies only (ForwardedHeaders with KnownProxies), at deploy. Today it is the socket address.
            ClientKey(http),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = window, QueueLimit = 0 }));

    private static string ClientKey(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
