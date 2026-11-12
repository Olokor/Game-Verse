using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GameVerse.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef migrations add`, which needs a context without starting the API.
/// Creating a migration never connects, so the connection string here is a placeholder.
/// Applying migrations goes through `make migrate` (the API's `migrate` command) instead.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GameVerseDbContext>
{
    public GameVerseDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GameVerseDbContext>();
        InfrastructureServices.ConfigureNpgsql(options, "Host=localhost;Database=gameverse_design_time");
        return new GameVerseDbContext(options.Options);
    }
}
