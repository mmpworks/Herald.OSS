using Microsoft.Extensions.Configuration;
using Serilog;

namespace Ref1.Worker;

// Ref1.Worker — the bread-and-butter Serilog case:
// Log.* static API, Console + File sinks, configured from appsettings.json
// via ReadFrom.Configuration. This is the most common production shape.
internal static class Program
{
    private static int Main()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();

        try
        {
            Log.Information("Worker starting at {StartedAt}", DateTimeOffset.UtcNow);

            for (int order = 1; order <= 3; order++)
            {
                Log.Information("Processing order {OrderId} for {Customer}", order, "Acme Corp");
            }

            Log.Warning("Queue depth {Depth} above soft limit {Limit}", 42, 25);
            Log.Error("Failed to reach {Endpoint} after {Attempts} attempts", "billing-svc", 3);
            Log.Information("Worker completed");
            return 0;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
