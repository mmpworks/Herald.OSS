using MMP.Herald.Serilog;
using Herald.OSS.Serilog.Expressions.Filtering; // .Filter.ByExcluding(string) extension lives here

namespace Ref4.Filtering;

// Ref4.Filtering (migrated) — the advanced case, now run-faithful.
//
// Both boundaries that previously diverged from the Serilog baseline are closed:
//
//   * Destructure.ByTransforming<Customer> redaction now holds on the NATIVE
//     WriteTo.Console() sink. The policy is applied at property-capture time
//     (before any sink), so the ApiKey is stripped sink-independently — same as
//     real Serilog. (Was: bypassed on native sinks, secret leaked.)
//
//   * .Filter.ByExcluding("RequestPath like '/health%'") is now a fluent step on
//     LoggerConfiguration (string-DSL extension from MMP.Herald.Serilog.Expressions),
//     and it drops the /health line end-to-end. (Was: no .Filter property, line printed.)
//
// Migration vehicle: one-namespace find-replace (using Serilog -> using MMP.Herald.Serilog)
// plus the added using for the expressions Filter extension. The rendered text format
// differs from Serilog's default (Herald's console template), but the level/message/
// filtered-set behaviour matches the baseline.
public sealed record Customer(string Name, string Email, string ApiKey);

internal static class Program
{
    private static int Main()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            // Redaction: strip ApiKey. Holds on the native console sink now.
            .Destructure.ByTransforming<Customer>(c => new { c.Name, c.Email })
            // Drop health-check noise — fluent string-DSL filter, applied end-to-end.
            .Filter.ByExcluding("RequestPath like '/health%'")
            .WriteTo.Console()
            .CreateLogger();

        var customer = new Customer("Ada", "ada@acme.test", "sk_live_SECRET");
        Log.Information("Customer registered {@Customer}", customer); // ApiKey stripped

        Log.Information("Request {RequestPath} served", "/orders");     // printed
        Log.Information("Request {RequestPath} served", "/health/live"); // dropped by the filter

        Log.CloseAndFlush();
        return 0;
    }
}
