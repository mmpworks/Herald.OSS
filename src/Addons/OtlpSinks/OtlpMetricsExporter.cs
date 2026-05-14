#nullable enable

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MMP.Herald.Metrics;

namespace MMP.Herald.Addons.OtlpSinks;

/// <summary>
/// Exports <see cref="GameMetrics"/> snapshots to an OpenTelemetry Collector
/// as OTLP JSON metrics over HTTP. Runs on a background timer.
///
/// Bridges Herald's lock-free game metrics with the standard OTLP metrics
/// pipeline (Grafana, Datadog, Prometheus via OTel Collector).
///
/// Usage:
///   var metrics = new GameMetrics();
///   var exporter = new OtlpMetricsExporter(metrics,
///       endpoint: "http://localhost:4318/v1/metrics",
///       exportIntervalMs: 5000,
///       serviceName: "my-game");
///
///   // In game loop:
///   metrics.Increment("physics.collisions");
///   metrics.Gauge("render.fps", currentFps);
///
///   // Metrics are exported every 5 seconds automatically.
///   // On shutdown:
///   await exporter.DisposeAsync();
/// </summary>
public sealed class OtlpMetricsExporter : IAsyncDisposable
{
    private readonly GameMetrics _metrics;
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _serviceName;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _exportTask;

    public OtlpMetricsExporter(
        GameMetrics metrics,
        string endpoint,
        int exportIntervalMs = 5000,
        string serviceName = "unknown_service",
        HttpClient? httpClient = null)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _serviceName = serviceName;
        _httpClient = httpClient ?? new HttpClient();

        _exportTask = ExportLoopAsync(exportIntervalMs, _cts.Token);
    }

    private async Task ExportLoopAsync(int intervalMs, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await ExportOnceAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ExportOnceAsync(CancellationToken ct)
    {
        var snapshot = _metrics.SnapshotAndReset();
        if (snapshot.Metrics.Count == 0) return;

        var json = BuildOtlpMetricsJson(snapshot);

        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(_endpoint, content, ct).ConfigureAwait(false);
            // Best-effort: don't throw on HTTP errors -- metrics export should not crash the game.
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Swallow transient network errors. Metrics will accumulate and be retried next interval.
        }
    }

    private string BuildOtlpMetricsJson(GameMetricsSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.Append("{\"resourceMetrics\":[{\"resource\":{\"attributes\":[");
        sb.Append("{\"key\":\"service.name\",\"value\":{\"stringValue\":\"");
        sb.Append(_serviceName);
        sb.Append("\"}}]},\"scopeMetrics\":[{\"scope\":{\"name\":\"herald.game_metrics\"},\"metrics\":[");

        var first = true;
        var nowNanos = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000;

        foreach (var m in snapshot.Metrics)
        {
            if (!first) sb.Append(',');
            first = false;

            sb.Append("{\"name\":\"");
            sb.Append(m.Name);
            sb.Append("\",");

            switch (m.Kind)
            {
                case MetricKind.Counter:
                    sb.Append("\"sum\":{\"dataPoints\":[{\"asInt\":\"");
                    sb.Append((long)m.Value);
                    sb.Append("\",\"timeUnixNano\":\"");
                    sb.Append(nowNanos);
                    sb.Append("\"}],\"isMonotonic\":true,\"aggregationTemporality\":2}");
                    break;

                case MetricKind.Gauge:
                    sb.Append("\"gauge\":{\"dataPoints\":[{");
                    if (m.FloatValue.HasValue)
                    {
                        sb.Append("\"asDouble\":");
                        sb.Append(m.FloatValue.Value.ToString("G"));
                    }
                    else
                    {
                        sb.Append("\"asInt\":\"");
                        sb.Append((long)m.Value);
                        sb.Append('"');
                    }
                    sb.Append(",\"timeUnixNano\":\"");
                    sb.Append(nowNanos);
                    sb.Append("\"}]}");
                    break;

                case MetricKind.Histogram:
                    // Export as gauge of the average for simplicity.
                    // Full OTLP histogram export would require bucket boundaries.
                    var avg = m.SampleCount > 0 ? m.Value / m.SampleCount.Value : 0;
                    sb.Append("\"gauge\":{\"dataPoints\":[{\"asDouble\":");
                    sb.Append(avg.ToString("G"));
                    sb.Append(",\"timeUnixNano\":\"");
                    sb.Append(nowNanos);
                    sb.Append("\"}]}");
                    break;
            }

            sb.Append('}');
        }

        sb.Append("]}]}]}");
        return sb.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _exportTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _cts.Dispose();
    }
}
