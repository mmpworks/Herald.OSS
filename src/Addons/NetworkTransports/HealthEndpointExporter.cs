#nullable enable

using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MMP.Herald.Metrics;
using MMP.Herald.Pipeline;

namespace MMP.Herald.Addons.NetworkTransports;

/// <summary>
/// Exports pipeline health reports as JSON to an HTTP endpoint on a background timer.
/// Intended for integration with monitoring dashboards (Grafana, Datadog, custom ops pages).
///
/// The export is best-effort: network failures are silently swallowed.
/// Health monitoring should not itself become a source of failures.
///
/// Usage:
///   var exporter = new HealthEndpointExporter(
///       healthAggregator,
///       endpoint: "http://monitoring.internal/api/health",
///       intervalMs: 10_000,
///       serviceName: "game-server-01");
///
///   // Health reports are pushed every 10 seconds.
///   // On shutdown:
///   await exporter.DisposeAsync();
///
/// Exported JSON format:
///   {
///     "service": "game-server-01",
///     "timestamp": "2026-04-09T12:00:00Z",
///     "state": "Healthy",
///     "totalDelivered": 150000,
///     "totalFailures": 3,
///     "queueDepth": 12,
///     "sinks": [
///       { "name": "console", "state": "Healthy", "failures": 0 },
///       { "name": "otlp", "state": "Degraded", "failures": 3 }
///     ]
///   }
/// </summary>
public sealed class HealthEndpointExporter : IAsyncDisposable
{
    private readonly PipelineHealthAggregator _healthAggregator;
    private readonly System.Net.Http.HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _serviceName;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _exportTask;

    public HealthEndpointExporter(
        PipelineHealthAggregator healthAggregator,
        string endpoint,
        int intervalMs = 10_000,
        string serviceName = "unknown_service",
        System.Net.Http.HttpClient? httpClient = null)
    {
        _healthAggregator = healthAggregator ?? throw new ArgumentNullException(nameof(healthAggregator));
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _serviceName = serviceName;
        _httpClient = httpClient ?? new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        _exportTask = ExportLoopAsync(intervalMs, _cts.Token);
    }

    /// <summary>
    /// Take a one-shot health snapshot as JSON. Useful for on-demand health checks
    /// without the background timer (e.g., responding to an HTTP GET /health).
    /// </summary>
    public string GetHealthJson()
    {
        var report = _healthAggregator.GetReport();
        return BuildJson(report);
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
        try
        {
            var json = GetHealthJson();
            using var content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");
            using var _ = await _httpClient.PostAsync(_endpoint, content, ct).ConfigureAwait(false);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Best-effort: health export should never crash the game.
        }
    }

    private string BuildJson(PipelineHealthReport report)
    {
        var sb = new StringBuilder();
        sb.Append("{\"service\":\"");
        sb.Append(_serviceName);
        sb.Append("\",\"timestamp\":\"");
        sb.Append(DateTimeOffset.UtcNow.ToString("o"));
        sb.Append("\",\"state\":\"");
        sb.Append(report.AggregateState);
        sb.Append("\",\"totalDelivered\":");
        sb.Append(report.TotalEventsDelivered);
        sb.Append(",\"totalFailures\":");
        sb.Append(report.TotalFailures);

        if (report.QueueDepth.HasValue)
        {
            sb.Append(",\"queueDepth\":");
            sb.Append(report.QueueDepth.Value);
        }

        sb.Append(",\"sinks\":[");
        var first = true;
        foreach (var sink in report.SinkStatuses)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"name\":\"");
            sb.Append(sink.SinkName);
            sb.Append("\",\"state\":\"");
            sb.Append(sink.State);
            sb.Append("\",\"failures\":");
            sb.Append(sink.ConsecutiveFailures);
            sb.Append('}');
        }

        sb.Append("]}");
        return sb.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _exportTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _cts.Dispose();
    }
}
