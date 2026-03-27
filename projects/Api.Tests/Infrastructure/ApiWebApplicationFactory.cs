using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Api.Tests.Infrastructure;

public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    // Use a unique relational test database per factory instance so optimistic-concurrency behavior
    // is exercised realistically. Cleanup happens on disposal; abrupt test termination may leave
    // temp files behind, but the GUID-based file names avoid cross-test contamination.
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"capitalism-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:EventsCatalog"] = $"Data Source={_databasePath}",
                ["SeedData:AdminEmail"] = "admin@capitalism.local",
                ["SeedData:AdminDisplayName"] = "Platform Admin",
                ["SeedData:AdminPassword"] = "ChangeMe123!"
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        TryDeleteDatabase();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        TryDeleteDatabase();
    }

    private void TryDeleteDatabase()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
