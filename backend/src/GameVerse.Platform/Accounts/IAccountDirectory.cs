using Microsoft.AspNetCore.Identity;

namespace GameVerse.Platform.Accounts;

/// <summary>What other modules may ask about an account (CLAUDE.md 4.1: modules talk through public interfaces).</summary>
public interface IAccountDirectory
{
    /// <summary>
    /// Whether the player counts as under 18. Guests and accounts without a birth year always do, so chat stays off
    /// until an adult has said so (CLAUDE.md section 7). Unknown or deleted accounts count as minors too.
    /// </summary>
    Task<bool> IsMinorAsync(Guid userId, CancellationToken ct = default);

    /// <summary>A summary of the account, or null when it does not exist or was deleted.</summary>
    Task<AccountSummary?> GetSummaryAsync(Guid userId, CancellationToken ct = default);
}

public sealed record AccountSummary(Guid UserId, bool IsGuest, string? Email, bool EmailVerified, bool IsMinor, DateTimeOffset CreatedAt);

/// <summary>The age rule on its own, so it is unit-testable with any clock.</summary>
public static class AgeRule
{
    public const int AdultAge = 18;

    /// <summary>
    /// With only a birth year, someone born in (current year - 18) may still be 17. Treat them as a minor until the
    /// year after, which errs on the side of the stricter (chat-off) setting.
    /// </summary>
    public static bool IsMinor(int? birthYear, DateTimeOffset now) => birthYear is not { } year || now.Year - year <= AdultAge;
}

internal sealed class AccountDirectory(UserManager<AccountUser> users, TimeProvider time) : IAccountDirectory
{
    public async Task<bool> IsMinorAsync(Guid userId, CancellationToken ct = default)
    {
        var summary = await GetSummaryAsync(userId, ct).ConfigureAwait(false);
        return summary?.IsMinor ?? true;
    }

    public async Task<AccountSummary?> GetSummaryAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        if (user is null || user.DeletedAt is not null)
        {
            return null;
        }

        var isMinor = user.IsGuest || AgeRule.IsMinor(user.BirthYear, time.GetUtcNow());
        return new AccountSummary(user.Id, user.IsGuest, user.Email, user.EmailConfirmed, isMinor, user.CreatedAt);
    }
}
