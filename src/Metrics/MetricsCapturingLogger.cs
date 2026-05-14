#nullable enable

using System;
using System.Diagnostics;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;

namespace MMP.Herald.Metrics;

/// <summary>
/// Decorator that captures delivery timing and success/failure metrics for a sink.
/// Transparent: re-throws on failure so audit and retry behavior are not broken.
/// Placed as the outermost wrapper to capture total latency of the full decorated chain.
/// </summary>
public sealed class MetricsCapturingLogger : ILogger
{
    private readonly ILogger _inner;
    private readonly ILogMetricsCollector _collector;

    public MetricsCapturingLogger(ILogger inner, ILogMetricsCollector collector)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
    }

    public void Log(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            _inner.Log(logEvent);
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            _collector.RecordDelivery((long)elapsed.TotalMilliseconds);
        }
        catch
        {
            _collector.RecordFailure();
            throw;
        }
    }
}
