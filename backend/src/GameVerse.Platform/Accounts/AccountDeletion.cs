using GameVerse.Platform.Profiles;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GameVerse.Platform.Accounts;

/// <summary>
/// In-app account deletion (owner decision 2026-11-12; Google Play requires it). The account row stays so that ledger
/// entries and match history keep their references (audit, CLAUDE.md section 7), but everything personal is removed:
/// email, password, birth year, device secrets, codes and sessions; the profile becomes "Deleted player".
/// </summary>
internal sealed partial class AccountDeletion(
    UserManager<AccountUser> users,
    DbContext db,
    AuthService sessions,
    IProfileService profiles,
    TimeProvider time,
    ILogger<AccountDeletion> log)
{
    /// <summary>False when the account is gone already or the password (email accounts) is wrong.</summary>
    public async Task<bool> DeleteAsync(Guid userId, string? password, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        if (user is null || user.DeletedAt is not null)
        {
            return false;
        }

        if (!user.IsGuest)
        {
            if (await users.IsLockedOutAsync(user).ConfigureAwait(false) || !await users.CheckPasswordAsync(user, password ?? string.Empty).ConfigureAwait(false))
            {
                await users.AccessFailedAsync(user).ConfigureAwait(false);
                return false;
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        user.Email = null;
        user.NormalizedEmail = null;
        user.EmailConfirmed = false;
        user.UserName = "deleted-" + user.Id.ToString("N");
        user.PasswordHash = null;
        user.PhoneNumber = null;
        user.BirthYear = null;
        user.DeletedAt = time.GetUtcNow();
        await users.UpdateSecurityStampAsync(user).ConfigureAwait(false); // also saves the fields above

        await db.Set<GuestDevice>().Where(d => d.UserId == userId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.Set<EmailCode>().Where(c => c.UserId == userId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await sessions.RevokeAllSessionsAsync(userId, "deleted", ct).ConfigureAwait(false);
        await profiles.AnonymiseAsync(userId, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);

        LogDeleted(log, userId);
        return true;
    }

    [LoggerMessage(EventId = 1201, Level = LogLevel.Information, Message = "GV_AUTH account_deleted user={UserId}")]
    private static partial void LogDeleted(ILogger logger, Guid userId);
}
