using GameVerse.Platform.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GameVerse.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for the modular monolith. Modules contribute their own
/// IEntityTypeConfiguration classes (internal to the module); they are picked up here by assembly scan.
/// Table and column names are snake_case (EFCore.NamingConventions).
/// It is an Identity context because accounts use ASP.NET Core Identity (CLAUDE.md section 3); the Accounts module moves
/// Identity's tables into the "accounts" schema. Modules use it through the plain <see cref="DbContext"/> type
/// (registered by InfrastructureServices), since GameVerse.Platform cannot reference Infrastructure.
/// </summary>
public sealed class GameVerseDbContext(DbContextOptions<GameVerseDbContext> options)
    : IdentityDbContext<AccountUser, IdentityRole<Guid>, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder); // Identity's model first; module configurations below adjust it
        builder.ApplyConfigurationsFromAssembly(typeof(GameVerseDbContext).Assembly);
        builder.ApplyConfigurationsFromAssembly(typeof(Platform.PlatformModules).Assembly);
    }
}
