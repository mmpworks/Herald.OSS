using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Ref3.CustomExt;

// Ref3.CustomExt — the extension-author case: a source-compiled custom sink
// (ILogEventSink) and custom enricher (ILogEventEnricher), wired inline via the
// fluent API. No appsettings.json, no config-by-name — everything is in code.
// This is the shape that migrates to Herald with ZERO source change.

// Custom sink: writes a compact line and counts events it sees.
public sealed class CountingConsoleSink : ILogEventSink
{
    private int _count;
    public int Count => _count;

    public void Emit(LogEvent logEvent)
    {
        _count++;
        Console.WriteLine($"[SINK {_count:D2}] {logEvent.Level} :: {logEvent.RenderMessage()}");
    }
}

// Custom enricher: stamps every event with a fixed tenant property.
public sealed class TenantEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var prop = propertyFactory.CreateProperty("Tenant", "acme");
        logEvent.AddPropertyIfAbsent(prop);
    }
}

internal static class Program
{
    private static int Main()
    {
        var sink = new CountingConsoleSink();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.With(new TenantEnricher())
            .WriteTo.Sink(sink)
            .CreateLogger();

        Log.Debug("Boot sequence {Step}", "init");
        Log.Information("Order {OrderId} placed by {Customer}", 1001, "Acme Corp");
        Log.Warning("Latency {Ms}ms over budget {Budget}ms", 320, 250);
        Log.Error("Payment {PaymentId} declined", "pay_77");

        Log.Information("Custom sink saw {Count} events", sink.Count);
        Log.CloseAndFlush();
        return 0;
    }
}
