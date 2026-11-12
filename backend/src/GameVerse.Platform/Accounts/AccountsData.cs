using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameVerse.Platform.Accounts;

/// <summary>The hashed device secret that lets a guest sign back in on the device that created it.</summary>
internal sealed class GuestDevice
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>SHA-256 of the device secret. The secret itself is shown to the client once and never stored.</summary>
    public byte[] SecretHash { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}

/// <summary>
/// One refresh token. Tokens issued from the same sign-in share a <see cref="FamilyId"/>; each refresh marks the
/// presented token used and issues the next one in the family. Only the SHA-256 hash is stored.
/// </summary>
internal sealed class RefreshToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid FamilyId { get; set; }

    public byte[] TokenHash { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>logout, reuse, password_reset, deleted, retry_superseded.</summary>
    public string? RevokedReason { get; set; }
}

/// <summary>
/// A one-time 6-digit code sent by email (verify the address, reset the password). Stored as a salted SHA-256 hash;
/// valid 15 minutes and for at most 5 attempts, so the small code space cannot be guessed online.
/// </summary>
internal sealed class EmailCode
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>verify_email or reset_password.</summary>
    public string Purpose { get; set; } = string.Empty;

    public byte[] Salt { get; set; } = [];

    public byte[] CodeHash { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public int Attempts { get; set; }
}

// EF Core mappings (picked up by GameVerseDbContext's assembly scan). Identity's tables move into the "accounts" schema.
internal sealed class AccountUserConfiguration : IEntityTypeConfiguration<AccountUser>
{
    public void Configure(EntityTypeBuilder<AccountUser> builder)
    {
        builder.ToTable("users", AccountsSchema.Name);
        // Identity's email index is not unique (guests have no email). Make it unique where an email exists, so two
        // upgrades racing for one address cannot both win.
        builder.HasIndex(u => u.NormalizedEmail).IsUnique().HasFilter("normalized_email IS NOT NULL");
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder) => builder.ToTable("roles", AccountsSchema.Name);
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<IdentityUserRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserRole<Guid>> builder) => builder.ToTable("user_roles", AccountsSchema.Name);
}

internal sealed class UserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder) => builder.ToTable("user_claims", AccountsSchema.Name);
}

internal sealed class UserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder) => builder.ToTable("user_logins", AccountsSchema.Name);
}

internal sealed class UserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder) => builder.ToTable("user_tokens", AccountsSchema.Name);
}

internal sealed class RoleClaimConfiguration : IEntityTypeConfiguration<IdentityRoleClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRoleClaim<Guid>> builder) => builder.ToTable("role_claims", AccountsSchema.Name);
}

internal sealed class GuestDeviceConfiguration : IEntityTypeConfiguration<GuestDevice>
{
    public void Configure(EntityTypeBuilder<GuestDevice> builder)
    {
        builder.ToTable("guest_devices", AccountsSchema.Name);
        builder.HasKey(d => d.Id);
        builder.Property(d => d.SecretHash).HasMaxLength(32).IsRequired();
        builder.HasIndex(d => d.UserId);
        builder.HasOne<AccountUser>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens", AccountsSchema.Name);
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TokenHash).HasMaxLength(32).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.FamilyId);
        builder.HasIndex(t => t.UserId);
        builder.Property(t => t.RevokedReason).HasMaxLength(32);
        builder.HasOne<AccountUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EmailCodeConfiguration : IEntityTypeConfiguration<EmailCode>
{
    public void Configure(EntityTypeBuilder<EmailCode> builder)
    {
        builder.ToTable("email_codes", AccountsSchema.Name);
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Purpose).HasMaxLength(32).IsRequired();
        builder.Property(c => c.Salt).HasMaxLength(16).IsRequired();
        builder.Property(c => c.CodeHash).HasMaxLength(32).IsRequired();
        builder.HasIndex(c => new { c.UserId, c.Purpose });
        builder.HasOne<AccountUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal static class AccountsSchema
{
    public const string Name = "accounts";
}
