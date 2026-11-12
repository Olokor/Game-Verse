using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GameVerse.Platform.Accounts;

/// <summary>Tokens issued for one sign-in: access token plus the plaintext refresh token (shown to the client once).</summary>
internal sealed record IssuedTokens(
    AccountUser User, string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

/// <summary>
/// Guest accounts and sessions (ADR-0006). A session is a refresh-token family: every refresh rotates the token, and a
/// token presented again after the retry grace window revokes the whole family (it has been copied).
/// </summary>
internal sealed partial class AuthService(
    UserManager<AccountUser> users,
    DbContext db,
    AccessTokenIssuer accessTokens,
    AccountsOptions options,
    TimeProvider time,
    ILogger<AuthService> log)
{
    public async Task<(IssuedTokens Tokens, string DeviceSecret)> CreateGuestAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var user = new AccountUser
        {
            Id = Guid.CreateVersion7(now),
            UserName = "guest-" + Guid.NewGuid().ToString("N"),
            IsGuest = true,
            CreatedAt = now,
        };
        var created = await users.CreateAsync(user).ConfigureAwait(false);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException("Guest creation failed: " + string.Join("; ", created.Errors.Select(e => e.Code)));
        }

        var secret = AuthSecrets.New(AuthSecrets.DevicePrefix);
        db.Set<GuestDevice>().Add(new GuestDevice { Id = Guid.CreateVersion7(now), UserId = user.Id, SecretHash = AuthSecrets.Hash(secret), CreatedAt = now });
        var tokens = await StartSessionAsync(user, ct).ConfigureAwait(false);
        LogGuestCreated(log, user.Id);
        return (tokens, secret);
    }

    public async Task<IssuedTokens?> GuestLoginAsync(Guid guestId, string deviceSecret, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(guestId.ToString()).ConfigureAwait(false);
        if (user is null || !user.IsGuest || user.DeletedAt is not null)
        {
            return null;
        }

        var devices = await db.Set<GuestDevice>().Where(d => d.UserId == guestId).ToListAsync(ct).ConfigureAwait(false);
        var device = devices.FirstOrDefault(d => AuthSecrets.Matches(deviceSecret, d.SecretHash));
        if (device is null)
        {
            LogGuestLoginFailed(log, guestId);
            return null;
        }

        device.LastUsedAt = time.GetUtcNow();
        return await StartSessionAsync(user, ct).ConfigureAwait(false);
    }

    /// <summary>Starts a new session (a new refresh-token family) and saves pending changes.</summary>
    public Task<IssuedTokens> StartSessionAsync(AccountUser user, CancellationToken ct) => IssueAsync(user, Guid.NewGuid(), ct);

    public async Task<IssuedTokens?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var hash = AuthSecrets.Hash(refreshToken);
        var token = await db.Set<RefreshToken>().SingleOrDefaultAsync(t => t.TokenHash == hash, ct).ConfigureAwait(false);
        if (token is null)
        {
            return null;
        }

        switch (RefreshTokenRules.Evaluate(token.UsedAt, token.RevokedAt, token.ExpiresAt, now, options.RefreshRetryGrace))
        {
            case RefreshVerdict.Rotate:
                // Conditional update: if two requests race with the same token, only one wins the rotation.
                var claimed = await db.Set<RefreshToken>()
                    .Where(t => t.Id == token.Id && t.UsedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct).ConfigureAwait(false);
                if (claimed == 0)
                {
                    return await RefreshAsync(refreshToken, ct).ConfigureAwait(false); // lost the race: re-evaluate
                }

                break;

            case RefreshVerdict.RetryWithinGrace:
                // The client probably never got the previous response. If the token it would have received is still
                // unused, retire it and issue another; if it was already used, the family has forked: treat as reuse.
                var successors = await db.Set<RefreshToken>()
                    .Where(t => t.FamilyId == token.FamilyId && t.CreatedAt >= token.CreatedAt && t.Id != token.Id && t.RevokedAt == null)
                    .ToListAsync(ct).ConfigureAwait(false);
                if (successors.Any(t => t.UsedAt is not null))
                {
                    await RevokeFamilyAsync(token, "reuse", ct).ConfigureAwait(false);
                    return null;
                }

                foreach (var successor in successors)
                {
                    successor.RevokedAt = now;
                    successor.RevokedReason = "retry_superseded";
                }

                break;

            case RefreshVerdict.Reuse:
                await RevokeFamilyAsync(token, "reuse", ct).ConfigureAwait(false);
                return null;

            default:
                return null;
        }

        var user = await users.FindByIdAsync(token.UserId.ToString()).ConfigureAwait(false);
        if (user is null || user.DeletedAt is not null)
        {
            return null;
        }

        return await IssueAsync(user, token.FamilyId, ct).ConfigureAwait(false);
    }

    /// <summary>Ends the session the refresh token belongs to. Unknown tokens are ignored (logout is idempotent).</summary>
    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        var hash = AuthSecrets.Hash(refreshToken);
        var token = await db.Set<RefreshToken>().SingleOrDefaultAsync(t => t.TokenHash == hash, ct).ConfigureAwait(false);
        if (token is not null)
        {
            await RevokeFamilyAsync(token, "logout", ct, logAsTheft: false).ConfigureAwait(false);
        }
    }

    /// <summary>Revokes every session of a user (password reset, account deletion).</summary>
    public async Task RevokeAllSessionsAsync(Guid userId, string reason, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await db.Set<RefreshToken>()
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, reason), ct)
            .ConfigureAwait(false);
    }

    private async Task RevokeFamilyAsync(RefreshToken token, string reason, CancellationToken ct, bool logAsTheft = true)
    {
        var now = time.GetUtcNow();
        await db.Set<RefreshToken>()
            .Where(t => t.FamilyId == token.FamilyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, reason), ct)
            .ConfigureAwait(false);
        if (logAsTheft)
        {
            LogRefreshReuse(log, token.UserId, token.FamilyId);
        }
    }

    private async Task<IssuedTokens> IssueAsync(AccountUser user, Guid familyId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var (access, accessExpires) = accessTokens.Issue(user);
        var refresh = AuthSecrets.New(AuthSecrets.RefreshPrefix);
        var refreshExpires = now + options.RefreshTokenLifetime;
        db.Set<RefreshToken>().Add(new RefreshToken
        {
            Id = Guid.CreateVersion7(now),
            UserId = user.Id,
            FamilyId = familyId,
            TokenHash = AuthSecrets.Hash(refresh),
            CreatedAt = now,
            ExpiresAt = refreshExpires,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new IssuedTokens(user, access, accessExpires, refresh, refreshExpires);
    }

    // Structured security events (no PII: ids only). Warnings reach GlitchTip as breadcrumbs, errors as events.
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "GV_AUTH guest_created user={UserId}")]
    private static partial void LogGuestCreated(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "GV_AUTH guest_login_failed user={UserId}")]
    private static partial void LogGuestLoginFailed(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Error, Message = "GV_AUTH refresh_reuse user={UserId} family={FamilyId}: session revoked")]
    private static partial void LogRefreshReuse(ILogger logger, Guid userId, Guid familyId);
}
