using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace MasterApi.Tests.Infrastructure;

public sealed class MasterApiWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MasterServer:RegistrationKey"] = "test-registration-key",
                ["MasterServer:ActiveThresholdSeconds"] = "90",
            });
        });
    }
}
