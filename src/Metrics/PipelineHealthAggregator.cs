#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using MMP.Herald.Pipeline;

namespace MMP.Herald.Metrics;

/// <summary>
/// Aggregates health status from all sinks and metrics collectors
/// into a unified PipelineHealthReport. Queryable at any time for
/// operational visibility.
/// </summary>
public sealed class PipelineHealthAggregator
{
    private readonly IReadOnlyList<ISinkHealthReporter> _healthReporters;
    private readonly IReadOnlyList<LogMetricsCollector> _metricsCollectors;
    private readonly AsyncLogger? _asyncLogger;

    public PipelineHealthAggregator(
        IReadOnlyList<ISinkHealthReporter> healthReporters,
        IReadOnlyList<LogMetricsCollector>? metricsCollectors = null,
        AsyncLogger? asyncLogger = null) {
        _healthReporters = healthReporters ?? throw new ArgumentNullException(nameof(healthReporters));
        _metricsCollectors = metricsCollectors ?? [];
        _asyncLogger = asyncLogger;
    }

    public PipelineHealthReport GetReport() {
        var sinkStatuses = new List<SinkHealthStatus>(_healthReporters.Count);

        foreach (var reporter in _healthReporters)
        {
            sinkStatuses.Add(reporter.GetHealthStatus());
        }

        // Aggregate state: worst-sink-wins
        var aggregateState = SinkState.Healthy;
        foreach (var status in sinkStatuses)
        {
            if (status.State == SinkState.Unhealthy)
            {
                aggregateState = SinkState.Unhealthy;
                break;
            }

            if (status.State == SinkState.Degraded)
            {
                aggregateState = SinkState.Degraded;
            }
        }

        // Aggregate metrics
        long totalDelivered = 0;
        long totalFailures = 0;

        foreach (var collector in _metricsCollectors)
        {
            var snapshot = collector.GetSnapshot();
            totalDelivered += snapshot.EventCount;
            totalFailures += snapshot.FailureCount;
        }

        return new PipelineHealthReport(
            sinkStatuses,
            aggregateState,
            totalDelivered,
            totalFailures,
            _asyncLogger?.QueueDepth);
    }
}
