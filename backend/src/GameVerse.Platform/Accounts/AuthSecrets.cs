using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace GameVerse.Platform.Accounts;

/// <summary>
/// Random credentials (device secrets, refresh tokens) and their storage hashes. Both are 32 random bytes, so a plain
/// SHA-256 is the right hash: there is nothing to brute-force, and a slow password hash would only cost CPU.
/// </summary>
internal static class AuthSecrets
{
    public const string RefreshPrefix = "gvr_";
    public const string DevicePrefix = "gvd_";

    /// <summary>A new secret: prefix + base64url of 32 random bytes. The prefix makes leaked values easy to scan for.</summary>
    public static string New(string prefix) => prefix + Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    /// <summary>Constant-time comparison, so response timing reveals nothing about how much of a secret matched.</summary>
    public static bool Matches(string secret, byte[] expectedHash) =>
        CryptographicOperations.FixedTimeEquals(Hash(secret), expectedHash);
}

internal enum RefreshVerdict
{
    /// <summary>Valid and unused: mark used and issue the next token in the family.</summary>
    Rotate,

    /// <summary>Used moments ago: most likely the client retried after losing the response. Rotate again.</summary>
    RetryWithinGrace,

    /// <summary>Used earlier than the grace window: a copy of the token is in someone else's hands. Revoke the family.</summary>
    Reuse,

    Expired,

    Revoked,
}

/// <summary>The refresh-token decision, separated from storage so it can be unit-tested exhaustively.</summary>
internal static class RefreshTokenRules
{
    public static RefreshVerdict Evaluate(
        DateTimeOffset? usedAt, DateTimeOffset? revokedAt, DateTimeOffset expiresAt, DateTimeOffset now, TimeSpan retryGrace)
    {
        if (revokedAt is not null)
        {
            return RefreshVerdict.Revoked;
        }

        if (usedAt is { } used)
        {
            return now - used <= retryGrace ? RefreshVerdict.RetryWithinGrace : RefreshVerdict.Reuse;
        }

        return expiresAt <= now ? RefreshVerdict.Expired : RefreshVerdict.Rotate;
    }
}
