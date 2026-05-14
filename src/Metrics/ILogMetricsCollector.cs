#nullable enable

namespace MMP.Herald.Metrics;

/// <summary>
/// Collects per-sink delivery metrics.
/// </summary>
public interface ILogMetricsCollector
{
    void RecordDelivery(long elapsedMilliseconds);
    void RecordFailure();

    /// <summary>
    /// Record one event that the pipeline dropped before delivery — queue
    /// overflow, sync-wait timeout, circuit-breaker open, cardinality-guard
    /// trip, sampling threshold exceeded. Drops are distinct from failures:
    /// a failure tried and was rejected by the sink, while a drop never
    /// reached the sink in the first place.
    /// </summary>
    void RecordDrop();

    /// <summary>
    /// Record one dropped event with a reason tag (see
    /// <see cref="DropReasons"/>). The aggregate <see cref="RecordDrop()"/>
    /// count still advances — this call adds a per-reason entry on top.
    /// Default implementation is a no-op on the per-reason side so existing
    /// collectors keep their aggregate behaviour without modification.
    /// </summary>
    void RecordDrop(string reason) { }

    LogMetricsSnapshot GetSnapshot();
}
