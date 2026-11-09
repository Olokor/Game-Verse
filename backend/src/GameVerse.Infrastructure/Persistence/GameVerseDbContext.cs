using Microsoft.EntityFrameworkCore;

namespace GameVerse.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for the modular monolith. Modules contribute their own
/// IEntityTypeConfiguration classes (internal to the module); they are picked up here by assembly scan.
/// Table and column names are snake_case (EFCore.NamingConventions).
/// </summary>
public sealed class GameVerseDbContext(DbContextOptions<GameVerseDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GameVerseDbContext).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Platform.PlatformModules).Assembly);
    }
}
