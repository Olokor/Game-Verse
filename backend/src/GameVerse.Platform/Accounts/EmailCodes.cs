using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace GameVerse.Platform.Accounts;

internal enum CodeCheck
{
    Valid,
    Invalid,
    Expired,
    TooManyAttempts,
}

/// <summary>One-time email codes (ADR-0006). Issuing a new code replaces any open code for the same purpose.</summary>
internal sealed class EmailCodes(DbContext db, TimeProvider time)
{
    public const string VerifyEmail = "verify_email";
    public const string ResetPassword = "reset_password";
    public const int MaxAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    /// <summary>Creates a code and returns it in plaintext (for the email only). Saves immediately.</summary>
    public async Task<string> IssueAsync(Guid userId, string purpose, CancellationToken ct)
    {
        await db.Set<EmailCode>().Where(c => c.UserId == userId && c.Purpose == purpose).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        var now = time.GetUtcNow();
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var salt = RandomNumberGenerator.GetBytes(16);
        db.Set<EmailCode>().Add(new EmailCode
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            Purpose = purpose,
            Salt = salt,
            CodeHash = Hash(salt, code),
            CreatedAt = now,
            ExpiresAt = now + Lifetime,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return code;
    }

    /// <summary>Checks a code. A valid code is consumed; a wrong one uses up an attempt.</summary>
    public async Task<CodeCheck> CheckAsync(Guid userId, string purpose, string code, CancellationToken ct)
    {
        var stored = await db.Set<EmailCode>()
            .Where(c => c.UserId == userId && c.Purpose == purpose)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (stored is null)
        {
            return CodeCheck.Invalid;
        }

        if (stored.ExpiresAt <= time.GetUtcNow())
        {
            return CodeCheck.Expired;
        }

        if (stored.Attempts >= MaxAttempts)
        {
            return CodeCheck.TooManyAttempts;
        }

        if (!CryptographicOperations.FixedTimeEquals(Hash(stored.Salt, code), stored.CodeHash))
        {
            stored.Attempts++;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return stored.Attempts >= MaxAttempts ? CodeCheck.TooManyAttempts : CodeCheck.Invalid;
        }

        db.Set<EmailCode>().Remove(stored);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return CodeCheck.Valid;
    }

    private static byte[] Hash(byte[] salt, string code) => SHA256.HashData([.. salt, .. Encoding.UTF8.GetBytes(code)]);
}

/// <summary>Account emails. Plain text first; the HTML is the same words with minimal markup and no external content.</summary>
internal static class AccountEmails
{
    public static EmailMessage VerifyEmail(string to, string code) => Message(
        to,
        "Your GameVerse verification code: " + code,
        "Use this code to confirm your email for GameVerse",
        code,
        "It expires in 15 minutes. If you did not create a GameVerse account, ignore this email.");

    public static EmailMessage ResetPassword(string to, string code) => Message(
        to,
        "Your GameVerse password reset code: " + code,
        "Use this code to set a new GameVerse password",
        code,
        "It expires in 15 minutes and signs you out everywhere. If you did not ask for this, ignore this email; your password stays the same.");

    private static EmailMessage Message(string to, string subject, string lead, string code, string footer)
    {
        var text = $"{lead}:\n\n    {code}\n\n{footer}\n\nGameVerse\n";
        var html =
            "<!doctype html><html><body style=\"font-family:sans-serif;line-height:1.5\">" +
            $"<p>{lead}:</p><p style=\"font-size:28px;font-weight:bold;letter-spacing:4px\">{code}</p>" +
            $"<p>{footer}</p><p>GameVerse</p></body></html>";
        return new EmailMessage(to, subject, text, html);
    }
}
