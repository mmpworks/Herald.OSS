#nullable enable

using System;

namespace MMP.Herald.Spans;

/// <summary>
/// Receives span duration metrics when spans complete.
/// Implementations decide what to do with the data:
/// emit to Prometheus, push to a profiler, aggregate in memory, etc.
///
/// Injected into LogSpanFactory at construction time.
/// When not provided, span metrics are silently skipped.
///
/// Usage:
///   public sealed class PrometheusSpanCollector : ISpanMetricsCollector
///   {
///       public void RecordSpanDuration(string spanName, TimeSpan duration, string? parentSpanId)
///       {
///           histogram.Observe(spanName, duration.TotalMilliseconds);
///       }
///   }
/// </summary>
public interface ISpanMetricsCollector
{
    /// <summary>
    /// Called when a span completes. Implementations must not throw.
    /// </summary>
    /// <param name="spanName">The human-readable span name.</param>
    /// <param name="duration">Wall-clock duration of the span.</param>
    /// <param name="parentSpanId">Null for root spans.</param>
    void RecordSpanDuration(string spanName, TimeSpan duration, string? parentSpanId);
}
