using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GameVerse.Platform.Accounts;

internal enum UpgradeOutcome
{
    Upgraded,
    NotAGuest,
    EmailTaken,
    InvalidPassword,
    AccountGone,
}

/// <summary>
/// Email accounts (ADR-0006): upgrading a guest in place, email verification, email sign-in with lockout, and password
/// reset. Codes are mailed through <see cref="IEmailSender"/>; a failed send never fails the request (the player can
/// ask for another code), but it is logged at Error so it reaches GlitchTip.
/// </summary>
internal sealed partial class EmailAccountService(
    UserManager<AccountUser> users,
    DbContext db,
    AuthService sessions,
    EmailCodes codes,
    IEmailSender email,
    ILogger<EmailAccountService> log)
{
    public async Task<(UpgradeOutcome Outcome, IssuedTokens? Tokens, IEnumerable<string> Errors)> UpgradeAsync(
        Guid userId, string address, string password, int birthYear, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        if (user is null || user.DeletedAt is not null)
        {
            return (UpgradeOutcome.AccountGone, null, []);
        }

        if (!user.IsGuest)
        {
            return (UpgradeOutcome.NotAGuest, null, []);
        }

        if (await users.FindByEmailAsync(address).ConfigureAwait(false) is not null)
        {
            return (UpgradeOutcome.EmailTaken, null, []);
        }

        // One transaction: the password, email and flags change together or not at all. UserManager shares this
        // request's DbContext, so its saves join the transaction.
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var withPassword = await users.AddPasswordAsync(user, password).ConfigureAwait(false);
        if (!withPassword.Succeeded)
        {
            return (UpgradeOutcome.InvalidPassword, null, withPassword.Errors.Select(e => e.Description));
        }

        user.Email = address;
        user.EmailConfirmed = false;
        user.IsGuest = false;
        user.BirthYear = birthYear;
        try
        {
            var updated = await users.UpdateAsync(user).ConfigureAwait(false);
            if (!updated.Succeeded)
            {
                return (UpgradeOutcome.InvalidPassword, null, updated.Errors.Select(e => e.Description));
            }
        }
        catch (DbUpdateException)
        {
            return (UpgradeOutcome.EmailTaken, null, []); // lost a race for the address (unique index)
        }

        // The device secret has done its job: from now on the account signs in with email and password.
        await db.Set<GuestDevice>().Where(d => d.UserId == user.Id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await sessions.RevokeAllSessionsAsync(user.Id, "upgraded", ct).ConfigureAwait(false);
        var tokens = await sessions.StartSessionAsync(user, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);

        LogUpgraded(log, user.Id);
        await SendCodeAsync(user, EmailCodes.VerifyEmail, ct).ConfigureAwait(false);
        return (UpgradeOutcome.Upgraded, tokens, []);
    }

    /// <summary>Confirms the email with the mailed code. Valid on success, and also when it was already verified.</summary>
    public async Task<CodeCheck> VerifyEmailAsync(Guid userId, string code, CancellationToken ct)
    {
        var user = await ActiveEmailUserAsync(userId).ConfigureAwait(false);
        if (user is null)
        {
            return CodeCheck.Invalid;
        }

        if (user.EmailConfirmed)
        {
            return CodeCheck.Valid;
        }

        var check = await codes.CheckAsync(user.Id, EmailCodes.VerifyEmail, code, ct).ConfigureAwait(false);
        if (check == CodeCheck.Valid)
        {
            user.EmailConfirmed = true;
            await users.UpdateAsync(user).ConfigureAwait(false);
        }

        return check;
    }

    public async Task ResendVerificationAsync(Guid userId, CancellationToken ct)
    {
        var user = await ActiveEmailUserAsync(userId).ConfigureAwait(false);
        if (user is { EmailConfirmed: false })
        {
            await SendCodeAsync(user, EmailCodes.VerifyEmail, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Email sign-in. Null for any failure (unknown email, guest, wrong password, locked out), so responses never reveal
    /// which. Five wrong passwords lock the account for 15 minutes.
    /// </summary>
    public async Task<IssuedTokens?> LoginAsync(string address, string password, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(address).ConfigureAwait(false);
        if (user is null || user.IsGuest || user.DeletedAt is not null)
        {
            return null;
        }

        if (await users.IsLockedOutAsync(user).ConfigureAwait(false))
        {
            LogLockedOut(log, user.Id);
            return null;
        }

        if (!await users.CheckPasswordAsync(user, password).ConfigureAwait(false))
        {
            await users.AccessFailedAsync(user).ConfigureAwait(false);
            return null;
        }

        await users.ResetAccessFailedCountAsync(user).ConfigureAwait(false);
        return await sessions.StartSessionAsync(user, ct).ConfigureAwait(false);
    }

    /// <summary>Mails a reset code if the address belongs to an email account. Silent either way.</summary>
    public async Task ForgotPasswordAsync(string address, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(address).ConfigureAwait(false);
        if (user is { IsGuest: false, DeletedAt: null })
        {
            await SendCodeAsync(user, EmailCodes.ResetPassword, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sets a new password with the mailed code, signs out every session, clears any lockout, and marks the email
    /// verified (receiving the code proves the player controls the address).
    /// </summary>
    public async Task<(CodeCheck Check, IEnumerable<string> Errors)> ResetPasswordAsync(string address, string code, string newPassword, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(address).ConfigureAwait(false);
        if (user is null || user.IsGuest || user.DeletedAt is not null)
        {
            return (CodeCheck.Invalid, []);
        }

        foreach (var validator in users.PasswordValidators)
        {
            var valid = await validator.ValidateAsync(users, user, newPassword).ConfigureAwait(false);
            if (!valid.Succeeded)
            {
                return (CodeCheck.Valid, valid.Errors.Select(e => e.Description)); // code untouched: the player can retry
            }
        }

        var check = await codes.CheckAsync(user.Id, EmailCodes.ResetPassword, code, ct).ConfigureAwait(false);
        if (check != CodeCheck.Valid)
        {
            return (check, []);
        }

        await users.RemovePasswordAsync(user).ConfigureAwait(false);
        var set = await users.AddPasswordAsync(user, newPassword).ConfigureAwait(false);
        if (!set.Succeeded)
        {
            return (CodeCheck.Valid, set.Errors.Select(e => e.Description));
        }

        user.EmailConfirmed = true;
        await users.SetLockoutEndDateAsync(user, null).ConfigureAwait(false);
        await users.ResetAccessFailedCountAsync(user).ConfigureAwait(false);
        await sessions.RevokeAllSessionsAsync(user.Id, "password_reset", ct).ConfigureAwait(false);
        LogPasswordReset(log, user.Id);
        return (CodeCheck.Valid, []);
    }

    private async Task<AccountUser?> ActiveEmailUserAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        return user is { IsGuest: false, DeletedAt: null, Email: not null } ? user : null;
    }

    private async Task SendCodeAsync(AccountUser user, string purpose, CancellationToken ct)
    {
        var code = await codes.IssueAsync(user.Id, purpose, ct).ConfigureAwait(false);
        var message = purpose == EmailCodes.VerifyEmail ? AccountEmails.VerifyEmail(user.Email!, code) : AccountEmails.ResetPassword(user.Email!, code);
        try
        {
            await email.SendAsync(message, ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // any SMTP failure: the player can request another code; the request itself succeeded
        catch (Exception error)
#pragma warning restore CA1031
        {
            LogSendFailed(log, error, user.Id, purpose);
        }
    }

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "GV_AUTH upgraded user={UserId}")]
    private static partial void LogUpgraded(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Warning, Message = "GV_AUTH locked_out user={UserId}")]
    private static partial void LogLockedOut(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Information, Message = "GV_AUTH password_reset user={UserId}: all sessions revoked")]
    private static partial void LogPasswordReset(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 1104, Level = LogLevel.Error, Message = "GV_EMAIL send_failed user={UserId} purpose={Purpose}")]
    private static partial void LogSendFailed(ILogger logger, Exception error, Guid userId, string purpose);
}
