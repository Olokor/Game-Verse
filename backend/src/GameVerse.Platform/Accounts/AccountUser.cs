using Microsoft.AspNetCore.Identity;

namespace GameVerse.Platform.Accounts;

/// <summary>
/// The Identity user. Public only because the shared <c>GameVerseDbContext</c> (Infrastructure) derives from
/// <c>IdentityDbContext&lt;AccountUser, ...&gt;</c>, and a public type cannot expose an internal one. Other modules must
/// not use it: they go through <see cref="IAccountDirectory"/> (an architecture test enforces this).
/// </summary>
public sealed class AccountUser : IdentityUser<Guid>
{
    /// <summary>Guest accounts are device-bound and have no email (ADR-0006).</summary>
    public bool IsGuest { get; set; }

    /// <summary>Birth year, asked when a guest upgrades (owner decision 2026-11-12). Null for guests.</summary>
    public int? BirthYear { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set when the player deletes the account; the row stays, anonymised, for audit and foreign keys.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
