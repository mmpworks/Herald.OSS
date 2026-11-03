using System.Diagnostics;
using MMP.Herald.Addons.Observability;
using MMP.Herald.Enrichers;
using MMP.Herald.Events;
using MMP.Herald.Quick;

namespace LoggingPractices.Practices;

/// <summary>
/// Practice 4. Put context in every entry. ActivityEnricher stamps the current
/// trace id and span id on each event (WithEnrichers adds it to the pipeline).
/// TraceContextPropagator starts the trace and writes the W3C traceparent header.
/// </summary>
public static class ContextInEveryEntry
{
    public static async Task RunAsync()
    {
        // .NET creates activities only when a listener asks for them.
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var capture = new CaptureLogger(print: false);
        await using (var result = QuickLogBuilder.Create()
            .WithoutConsoleSink()
            .WithEnrichers(new ActivityEnricher())
            .WithBridge(capture)
            .BuildAndCommit())
        {
            using var trace = TraceContextPropagator.StartTrace("checkout");
            var headers = new Dictionary<string, string>();
            TraceContextPropagator.Inject(headers);
            Console.WriteLine($"  outgoing header traceparent: {headers["traceparent"]}");

            result.Logger.Information(LogCategory.App, "Charging card for order {OrderId}", "A-17");
        }

        foreach (var e in capture.Events)
        {
            Console.WriteLine("  " + CaptureLogger.Describe(e));
            foreach (var (key, value) in e.Context)
                Console.WriteLine($"    context {key} = {value}");
        }
    }
}
