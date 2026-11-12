using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GameVerse.Platform.Accounts;

/// <summary>
/// The ES256 key that signs access tokens. Asymmetric so that a future realtime game server can verify tokens with the
/// public key alone (CLAUDE.md section 3). Created once at startup.
/// </summary>
public sealed class AccountsSigningKey : IDisposable
{
    private readonly ECDsa ecdsa;

    private AccountsSigningKey(ECDsa ecdsa, bool isEphemeral)
    {
        this.ecdsa = ecdsa;
        IsEphemeral = isEphemeral;
        SecurityKey = new ECDsaSecurityKey(ecdsa) { KeyId = isEphemeral ? "ephemeral" : "gameverse-1" };
    }

    public SecurityKey SecurityKey { get; }

    /// <summary>True when generated at startup (Development only): tokens die with the process.</summary>
    public bool IsEphemeral { get; }

    /// <summary>Loads the PEM, or generates a throwaway key when allowed (Development, OpenAPI export).</summary>
    public static AccountsSigningKey Create(string? pem, bool allowEphemeral)
    {
        var ecdsa = ECDsa.Create();
        if (!string.IsNullOrWhiteSpace(pem))
        {
            ecdsa.ImportFromPem(pem.Replace("\\n", "\n", StringComparison.Ordinal)); // env vars often carry escaped newlines
            if (ecdsa.KeySize != 256)
            {
                ecdsa.Dispose();
                throw new InvalidOperationException("GameVerse:Auth:SigningKey must be an ECDSA P-256 private key (ES256).");
            }

            return new AccountsSigningKey(ecdsa, isEphemeral: false);
        }

        if (!allowEphemeral)
        {
            ecdsa.Dispose();
            throw new InvalidOperationException(
                "GameVerse:Auth:SigningKey is required outside Development. Generate one with " +
                "`openssl ecparam -name prime256v1 -genkey -noout` and set GameVerse__Auth__SigningKey.");
        }

        ecdsa.GenerateKey(ECCurve.NamedCurves.nistP256);
        return new AccountsSigningKey(ecdsa, isEphemeral: true);
    }

    public void Dispose() => ecdsa.Dispose();
}

/// <summary>Issues access tokens: sub (account id), guest flag, jti. Validation is configured with the same key.</summary>
internal sealed class AccessTokenIssuer(AccountsSigningKey key, AccountsOptions options, TimeProvider time)
{
    public const string GuestClaim = "guest";

    private readonly JsonWebTokenHandler handler = new() { SetDefaultTimesOnTokenCreation = false };
    private readonly SigningCredentials credentials = new(key.SecurityKey, SecurityAlgorithms.EcdsaSha256);

    public (string Token, DateTimeOffset ExpiresAt) Issue(AccountUser user)
    {
        var now = time.GetUtcNow();
        var expires = now + options.AccessTokenLifetime;
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = credentials,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(GuestClaim, user.IsGuest ? "true" : "false", ClaimValueTypes.Boolean),
            ]),
        });
        return (token, expires);
    }

    public static TokenValidationParameters ValidationParameters(AccountsSigningKey key, AccountsOptions options) => new()
    {
        ValidIssuer = options.Issuer,
        ValidAudience = options.Audience,
        IssuerSigningKey = key.SecurityKey,
        ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = JwtRegisteredClaimNames.Sub,
    };
}

/// <summary>Reads the signed-in account id from the validated token.</summary>
internal static class AccountClaims
{
    public static Guid? UserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;
}
