#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace MMP.Herald.Metrics;

/// <summary>
/// Renders a <see cref="LogMetricsRegistry"/> snapshot as a Prometheus text
/// exposition format 0.0.4 payload. Drop-in for a `/metrics` handler:
///
/// <code>
/// app.MapGet("/metrics", () =>
/// {
///     var snapshots = result.MetricsRegistry.GetAllSnapshots();
///     var body = PrometheusMetricsRenderer.Render(snapshots);
///     return Results.Text(body, "text/plain; version=0.0.4; charset=utf-8");
/// });
/// </code>
///
/// <para>
/// Five counter families plus two gauges per sink: <c>herald_sink_events_total</c>,
/// <c>herald_sink_failures_total</c>, <c>herald_sink_drops_total</c>,
/// <c>herald_sink_latency_ms_sum</c>, <c>herald_sink_events_seen</c> (gauge
/// mirroring the counter for dashboards that need an instantaneous value),
/// and <c>herald_sink_latency_ms_avg</c>. Label values are escaped per the
/// exposition-format rules: backslash, double quote, and newline.
/// </para>
///
/// <para>
/// Consumers that want a different shape (OpenMetrics, histogram buckets,
/// native Otel metrics) can ignore this renderer and walk
/// <see cref="LogMetricsSnapshot"/> directly — the shape is small and the
/// snapshot is the real contract.
/// </para>
/// </summary>
public static class PrometheusMetricsRenderer
{
    /// <summary>Render every snapshot in the collection as a Prometheus text payload.</summary>
    public static string Render(IReadOnlyList<LogMetricsSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var sb = new StringBuilder(capacity: 512 + snapshots.Count * 256);
        AppendEventsTotal(sb, snapshots);
        AppendFailuresTotal(sb, snapshots);
        AppendDropsTotal(sb, snapshots);
        AppendFilterDropsTotal(sb, snapshots);
        AppendLatencySum(sb, snapshots);
        AppendLatencyAverage(sb, snapshots);
        return sb.ToString();
    }

    private static void AppendEventsTotal(StringBuilder sb, IReadOnlyList<LogMetricsSnapshot> snapshots)
    {
        sb.AppendLine("# HELP herald_sink_events_total Successfully delivered events per sink.");
        sb.AppendLine("# TYPE herald_sink_events_total counter");
        foreach (var s in snapshots)
        {
            sb.Append("herald_sink_events_total{sink=\"").Append(Escape(s.SinkName))
              .Append("\"} ").Append(s.EventCount).AppendLine();
        }
    }

    private static void AppendFailuresTotal(StringBuilder sb, IReadOnlyList<LogMetricsSnapshot> snapshots)
    {
        sb.AppendLine("# HELP herald_sink_failures_total Deliveries the sink rejected.");
        sb.AppendLine("# TYPE herald_sink_failures_total counter");
        foreach (var s in snapshots)
        {
            sb.Append("herald_sink_failures_total{sink=\"").Append(Escape(s.SinkName))
              .Append("\"} ").Append(s.FailureCount).AppendLine();
        }
    }

    private static void AppendDropsTotal(StringBuilder sb, IReadOnlyList<LogMetricsSnapshot> snapshots)
    {
        sb.AppendLine("# HELP herald_sink_drops_total Events the pipeline discarded before delivery (queue overflow, circuit-breaker open, sampling).");
        sb.AppendLine("# TYPE herald_sink_drops_total counter");
        foreach (var s in snapshots)
        {
            sb.Append("herald_sink_drops_total{sink=\"").Append(Escape(s.SinkName))
              .Append("\"} ").Append(s.DropCount).AppendLine();
        }
    }

    private static void AppendFilterDropsTotal(StringBuilder sb, IReadOnlyList<LogMetricsSnapshot> snapshots)
    {
        // Only emit the series when at least one snapshot carries a reason
        // breakdown — avoids printing a HELP/TYPE block with no samples on
        // pipelines that never use filter-side drop attribution.
        var anyReasons = false;
        foreach (var s in snapshots)
        {
            if (s.DropsByReason is { Count: > 0 }) { anyReasons = true; break; }
        }
        if (!anyReasons) return;

        sb.AppendLine("# HELP herald_sink_filter_drops_total Events a pipeline filter rejected, labelled by category (level, sampling, throttling, predicate).");
        sb.AppendLine("# TYPE herald_sink_filter_drops_total counter");
        foreach (var s in snapshots)
        {
            if (s.DropsByReason is not { Count: > 0 } reasons) continue;
            foreach (var kvp in reasons)
            {
                sb.Append("herald_sink_filter_drops_total{sink=\"").Append(Escape(s.SinkName))
                  .Append("\",reason=\"").Append(Escape(kvp.Key))
                  .Append("\"} ").Append(kvp.Value).AppendLine();
            }
        }
    }

    private static void AppendLatencySum(StringBuilder sb, IReadOnlyList<LogMetricsSnapshot> snapshots)
    {
        sb.AppendLine("# HELP herald_sink_latency_ms_sum Total end-to-end delivery latency per sink in milliseconds.");
        sb.AppendLine("# TYPE herald_sink_latency_ms_sum counter");
        foreach (var s in snapshots)
        {
            sb.Append("herald_sink_latency_ms_sum{sink=\"").Append(Escape(s.SinkName))
              .Append("\"} ").Append(s.TotalLatencyMs).AppendLine();
        }
    }

    private static void AppendLatencyAverage(StringBuilder sb, IReadOnlyList<LogMetricsSnapshot> snapshots)
    {
        sb.AppendLine("# HELP herald_sink_latency_ms_avg Running-average delivery latency per sink (total / event count).");
        sb.AppendLine("# TYPE herald_sink_latency_ms_avg gauge");
        foreach (var s in snapshots)
        {
            sb.Append("herald_sink_latency_ms_avg{sink=\"").Append(Escape(s.SinkName))
              .Append("\"} ").Append(s.AverageLatencyMs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
              .AppendLine();
        }
    }

    // Label values escape backslash, double quote, and newline per the
    // Prometheus exposition-format spec. Every other character passes through.
    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;

        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                default:   sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
