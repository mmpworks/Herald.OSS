using MMP.Herald.Serilog;
using Herald.OSS.Serilog.Expressions.Filtering; // Filter.ByExcluding(string) lives here

namespace Ref4.Filtering;

// Ref4.Filtering (migrated) — the advanced case.
//
// What carried over cleanly:
//   * Destructure.ByTransforming<Customer> compiles and is registered. IMPORTANT:
//     in 0.12.5 the policy only fires on the WriteTo.Sink(custom) mirror path; with the
//     native WriteTo.Console() below it is BYPASSED and the ApiKey LEAKS. This is the
//     measured truth, recorded — see FINDING-destructure-native-sink-leak.md. Under real
//     Serilog the same code redacts. Do not read this sample as "redaction carried over."
//
// The documented boundary (measured, not hidden):
//   * Real Serilog wires the expression filter as a FLUENT call on the config chain:
//       .Filter.ByExcluding("RequestPath like '/health%'")
//     Herald's LoggerConfiguration has no .Filter property, so that exact call site
//     does NOT compile. Herald DOES ship the string-DSL engine — Filter.ByExcluding
//     and Filter.ByIncludingOnly compile a string expression into an ILogFilter — but
//     it is not yet integrated as a LoggerConfiguration.Filter fluent step. So the
//     expression itself carries over; the fluent wiring is the gap. The filter object
//     below proves the DSL parses and compiles; applying it needs a Herald filter
//     integration point, which is the open seam for this case.
public sealed record Customer(string Name, string Email, string ApiKey);

internal static class Program
{
    private static int Main()
    {
        // The string-DSL expression itself carries over: it parses and compiles to an
        // ILogFilter. (Wiring it into the live pipeline is the documented boundary above.)
        var healthFilter = Filter.ByExcluding("RequestPath like '/health%'");
        _ = healthFilter;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            // Registered, but BYPASSED on the native console sink in 0.12.5 (secret leaks).
            .Destructure.ByTransforming<Customer>(c => new { c.Name, c.Email })
            .WriteTo.Console()
            .CreateLogger();

        var customer = new Customer("Ada", "ada@acme.test", "sk_live_SECRET");
        Log.Information("Customer registered {@Customer}", customer);

        Log.Information("Request {RequestPath} served", "/orders");
        // Without the .Filter wiring this line is NOT dropped — it prints. That is the
        // visible cost of the boundary, recorded honestly rather than papered over.
        Log.Information("Request {RequestPath} served", "/health/live");

        Log.CloseAndFlush();
        return 0;
    }
}
