using MMP.Herald.Addons.MetricExtraction;
using MMP.Herald.Events;
using MMP.Herald.Quick;
using MMP.Herald.Templating;

namespace LoggingPractices.Practices;

/// <summary>
/// Practice 12. Metrics say how often; logs say what happened.
/// LogMetricExtractor turns matching log events into counters and gauges as they
/// pass through the pipeline. OtlpMetricsExporter ships GameMetrics snapshots to an
/// OTLP collector on a timer.
/// </summary>
public static class LogsAreNotMetrics
{
    public static async Task RunAsync()
    {
        var extractor = new LogMetricExtractor(
        [
            new MetricRule("checkout.count", e => e.MessageTemplate.StartsWith("Checkout"), MetricType.Counter),
            new MetricRule("checkout.ms", e => e.MessageTemplate.StartsWith("Checkout"), MetricType.Gauge, PropertyName: "elapsedMs"),
        ]);

        await using (var result = QuickLogBuilder.Create()
            .WithoutConsoleSink()
            .WithNullSink()
            .WithEventProcessor(extractor)
            .BuildAndCommit())
        {
            foreach (var ms in new[] { 120.0, 95.0, 310.0, 88.0 })
                result.Logger.Information(LogCategory.App, "Checkout done in {elapsedMs} ms",
                    new LogProperty("elapsedMs", ms));
        }

        var snapshot = extractor.GetSnapshot();
        foreach (var (name, count) in snapshot.Counters)
            Console.WriteLine($"  counter {name} = {count}");
        foreach (var (name, gauge) in snapshot.Gauges)
            Console.WriteLine($"  gauge   {name}: count {gauge.Count}, average {gauge.Average:F1} ms");
    }
}
