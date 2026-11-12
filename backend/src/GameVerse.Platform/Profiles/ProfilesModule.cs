using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FluentValidation;
using GameVerse.Contracts.Accounts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using static GameVerse.Platform.PlatformHttp;

namespace GameVerse.Platform.Profiles;

/// <summary>What other modules may do with profiles (CLAUDE.md 4.1). Accounts creates and anonymises them.</summary>
public interface IProfileService
{
    /// <summary>Creates the default profile ("Player" + 4 digits, default avatar) for a new account. Saves.</summary>
    Task<ProfileSummary> CreateDefaultAsync(Guid userId, CancellationToken ct = default);

    Task<ProfileSummary?> GetAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Replaces the name and avatar with neutral values when the account is deleted. Saves.</summary>
    Task AnonymiseAsync(Guid userId, CancellationToken ct = default);
}

public sealed record ProfileSummary(Guid UserId, string DisplayName, string AvatarId);

/// <summary>Display-name rules: 3 to 20 letters, digits, single spaces or underscores, no leading or trailing space.</summary>
public static partial class DisplayNameRule
{
    public const int MinLength = 3;
    public const int MaxLength = 20;
    public const string DeletedName = "Deleted player";

    // TODO(gameverse): profanity and impersonation filter (Moderation module, CLAUDE.md section 7).
    public static bool IsValid(string? name) =>
        name is { Length: >= MinLength and <= MaxLength } && Allowed().IsMatch(name) && !name.Contains("  ", StringComparison.Ordinal);

    [GeneratedRegex(@"^[\p{L}\p{N}_](?:[\p{L}\p{N}_ ]*[\p{L}\p{N}_])?$")]
    private static partial Regex Allowed();
}

internal sealed class Profile
{
    public Guid UserId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string AvatarId { get; set; } = ProfileAvatars.Default;

    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class ProfileConfiguration : IEntityTypeConfiguration<Profile>
{
    // No foreign key to accounts.users: modules do not couple through each other's tables (CLAUDE.md 4.1).
    public void Configure(EntityTypeBuilder<Profile> builder)
    {
        builder.ToTable("profiles", "profiles");
        builder.HasKey(p => p.UserId);
        builder.Property(p => p.DisplayName).HasMaxLength(DisplayNameRule.MaxLength).IsRequired();
        builder.Property(p => p.AvatarId).HasMaxLength(32).IsRequired();
    }
}

internal sealed class ProfileService(DbContext db, TimeProvider time) : IProfileService
{
    public async Task<ProfileSummary> CreateDefaultAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = new Profile
        {
            UserId = userId,
            DisplayName = "Player" + RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4", CultureInfo.InvariantCulture),
            AvatarId = ProfileAvatars.Default,
            UpdatedAt = time.GetUtcNow(),
        };
        db.Set<Profile>().Add(profile);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Summary(profile);
    }

    public async Task<ProfileSummary?> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = await db.Set<Profile>().AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId, ct).ConfigureAwait(false);
        return profile is null ? null : Summary(profile);
    }

    public async Task<ProfileSummary?> UpdateAsync(Guid userId, string? displayName, string? avatarId, CancellationToken ct)
    {
        var profile = await db.Set<Profile>().SingleOrDefaultAsync(p => p.UserId == userId, ct).ConfigureAwait(false);
        if (profile is null)
        {
            return null;
        }

        profile.DisplayName = displayName ?? profile.DisplayName;
        profile.AvatarId = avatarId ?? profile.AvatarId;
        profile.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Summary(profile);
    }

    public async Task AnonymiseAsync(Guid userId, CancellationToken ct = default)
    {
        await db.Set<Profile>().Where(p => p.UserId == userId).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.DisplayName, DisplayNameRule.DeletedName)
                .SetProperty(p => p.AvatarId, ProfileAvatars.Default)
                .SetProperty(p => p.UpdatedAt, time.GetUtcNow()),
            ct).ConfigureAwait(false);
    }

    private static ProfileSummary Summary(Profile p) => new(p.UserId, p.DisplayName, p.AvatarId);
}

/// <summary>Registers the Profiles module and maps <c>/profiles/me</c>.</summary>
public static class ProfilesModule
{
    public static IServiceCollection AddProfilesModule(this IServiceCollection services)
    {
        services.AddScoped<ProfileService>();
        services.AddScoped<IProfileService>(sp => sp.GetRequiredService<ProfileService>());
        return services;
    }

    public static IEndpointRouteBuilder MapProfiles(this IEndpointRouteBuilder app)
    {
        var profiles = app.MapGroup("/profiles").WithTags("Profiles").RequireAuthorization();

        profiles.MapGet("/me", GetMeAsync)
            .WithSummary("The signed-in player's profile")
            .Produces<ProfileResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        profiles.MapPatch("/me", UpdateMeAsync)
            .Validate<UpdateProfileRequest>()
            .WithSummary("Change display name and/or avatar")
            .Produces<ProfileResponse>();

        return app;
    }

    private static async Task<IResult> GetMeAsync(HttpContext http, ProfileService profiles)
    {
        var profile = SignedInUser(http) is { } id ? await profiles.GetAsync(id, http.RequestAborted).ConfigureAwait(false) : null;
        return profile is null ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Profile not found") : Results.Ok(Response(profile));
    }

    private static async Task<IResult> UpdateMeAsync(UpdateProfileRequest request, HttpContext http, ProfileService profiles)
    {
        var profile = SignedInUser(http) is { } id
            ? await profiles.UpdateAsync(id, request.DisplayName, request.AvatarId, http.RequestAborted).ConfigureAwait(false)
            : null;
        return profile is null ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Profile not found") : Results.Ok(Response(profile));
    }

    private static Guid? SignedInUser(HttpContext http) => Guid.TryParse(http.User.FindFirst("sub")?.Value, out var id) ? id : null;

    private static ProfileResponse Response(ProfileSummary p) => new() { UserId = p.UserId.ToString(), DisplayName = p.DisplayName, AvatarId = p.AvatarId };
}

internal sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(r => r.DisplayName)
            .Must(DisplayNameRule.IsValid!)
            .When(r => r.DisplayName is not null)
            .WithMessage($"Use {DisplayNameRule.MinLength} to {DisplayNameRule.MaxLength} letters, numbers, spaces or underscores.");
        RuleFor(r => r.AvatarId)
            .Must(id => ProfileAvatars.All.Contains(id!))
            .When(r => r.AvatarId is not null)
            .WithMessage("Pick one of the available avatars.");
    }
}
