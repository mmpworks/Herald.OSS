using Serilog;

namespace Ref4.Filtering;

// Ref4.Filtering — the advanced case: a destructuring policy (ByTransforming) that
// redacts a secret field, plus a Serilog.Expressions string-DSL filter that drops
// health-check noise. Inline-wired; no appsettings.json.

public sealed record Customer(string Name, string Email, string ApiKey);

internal static class Program
{
    private static int Main()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            // Destructuring policy: project Customer to a redacted shape (no ApiKey).
            .Destructure.ByTransforming<Customer>(c => new { c.Name, c.Email })
            // Serilog.Expressions string-DSL filter: drop health-check log lines.
            .Filter.ByExcluding("RequestPath like '/health%'")
            .WriteTo.Console()
            .CreateLogger();

        var customer = new Customer("Ada", "ada@acme.test", "sk_live_SECRET");
        Log.Information("Customer registered {@Customer}", customer);

        Log.Information("Request {RequestPath} served", "/orders");
        Log.Information("Request {RequestPath} served", "/health/live");

        Log.CloseAndFlush();
        return 0;
    }
}
