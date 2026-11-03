using MMP.Herald.Addons.OtlpSinks;
using MMP.Herald.Events;
using MMP.Herald.Quick;

namespace LoggingPractices.Practices;

/// <summary>
/// Practice 7. Send every service's logs to one store over OTLP.
/// In production, one builder line points a pipeline at your collector:
/// <code>
///   .WithOtlpJsonSink("http://collector:4318/v1/logs")      // or WithOtlpProtobufSink
/// </code>
/// The sink itself ships in the MMP.Herald.Sinks.Otlp package (OtlpJsonLogSink,
/// OtlpProtobufLogSink); MMP.Herald.Sinks.OtlpGrpc adds OtlpGrpcLogSink.
/// This sample runs without a collector, so it prints the OpenTelemetry
/// LogRecord that OtelLogRecord builds from a Herald event.
/// </summary>
public static class AggregateAndCentralize
{
    public static async Task RunAsync()
    {
        var capture = new CaptureLogger(print: false);
        await using (var result = QuickLogBuilder.Create()
            .WithoutConsoleSink()
            .WithBridge(capture)
            .BuildAndCommit())
        {
            result.Logger.Warning(LogCategory.App, "Inventory low for {Sku}: {Left} left", "alpha", 3);
        }

        var record = OtelLogRecord.FromLogEvent(capture.Events[0], null);
        Console.WriteLine($"  timeUnixNano      {record.TimeUnixNano}");
        Console.WriteLine($"  severityNumber    {record.SeverityNumber}");
        Console.WriteLine($"  severityText      {record.SeverityText}");
        Console.WriteLine($"  body              {record.Body}");
        Console.WriteLine($"  scope             {record.InstrumentationScope}");
        foreach (var (key, value) in record.Attributes)
            Console.WriteLine($"  attribute         {key} = {value}");
    }
}
