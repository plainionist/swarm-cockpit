using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace SwarmCockpit.AcceptanceTests;

public sealed class CockpitFactory : WebApplicationFactory<Program>
{
    private readonly string _dbFile = Path.Combine(Path.GetTempPath(), $"swarm-cockpit-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var values = new Dictionary<string, string?>
            {
                ["Persistence:ConnectionString"] = $"Data Source={_dbFile}"
            };
            config.AddInMemoryCollection(values);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && File.Exists(_dbFile))
        {
            try
            {
                File.Delete(_dbFile);
            }
            catch (IOException)
            {
                // Test process shutdown can still hold an SQLite handle briefly.
            }
        }
    }
}
