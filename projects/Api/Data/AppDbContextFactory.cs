using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Api.Data;

/// <summary>
/// Design-time factory used by EF Core tooling (e.g. dotnet-ef migrations) to create
/// an <see cref="AppDbContext"/> instance with a SQLite provider when the normal
/// dependency-injection host is not running.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlite("Data Source=capitalism-design-time.db");
        return new AppDbContext(optionsBuilder.Options);
    }
}
