#nullable enable

namespace MMP.Herald.Metrics;

/// <summary>
/// A drop-reporting sink for pipeline decorators. Each call attributes one
/// event that the pipeline discarded before reaching a sink.
///
/// <para>
/// Decorators that drop events (queue overflow in
/// <see cref="MMP.Herald.Pipeline.AsyncLogger"/>, open-state reject in
/// <c>CircuitBreakerLogger</c> — plugin-supplied, lives in Herald.Pro) take an optional
/// <see cref="IPipelineDropSink"/> in their constructor. When wired (the
/// normal bootstrap path), drops land in the <see cref="LogMetricsRegistry"/>
/// and surface through <see cref="LogMetricsSnapshot.DropCount"/> — which
/// is what <see cref="PrometheusMetricsRenderer"/> exposes as
/// <c>herald_sink_drops_total</c>. When not wired (unit tests, one-off
/// constructions), the decorator works exactly as before; drops simply go
/// uncounted.
/// </para>
///
/// <para>
/// Pass <paramref name="sinkName"/> when the drop is attributable to a
/// specific named sink (the circuit breaker wrapping sink X dropped because
/// sink X was unreachable). Pass <c>null</c> when the drop happened upstream
/// of any specific sink (the pipeline's async queue overflowed and no sink
/// would have seen the event regardless); the implementation then
/// attributes the drop to every collector it knows about so the pipeline-
/// wide total still reflects reality.
/// </para>
/// </summary>
public interface IPipelineDropSink
{
    /// <summary>Record one dropped event. See interface docs for attribution rules.</summary>
    void RecordDrop(string? sinkName = null);

    /// <summary>
    /// Record one dropped event with a category tag (for example
    /// <see cref="DropReasons.Level"/>, <see cref="DropReasons.Sampling"/>).
    /// Filter-side rejections use this overload so the Prometheus exposition
    /// separates "dropped by filter" from "dropped by decorator".
    ///
    /// <para>
    /// Default implementation forwards to the unreason-tagged overload so
    /// older implementations continue to accumulate the aggregate drop count.
    /// Implementations that want per-reason attribution override this.
    /// </para>
    /// </summary>
    void RecordDrop(string? sinkName, string reason) => RecordDrop(sinkName);
}

/// <summary>
/// Canonical reason strings used as the <c>reason</c> label value in the
/// Prometheus exposition and on the per-reason snapshot map. Named here so
/// every filter and every test uses the same vocabulary.
/// </summary>
public static class DropReasons
{
    /// <summary>Event fell below the pipeline's level filter (static or dynamic).</summary>
    public const string Level = "level";
    /// <summary>Event rejected by a sampling filter (random-N, priority window, adaptive).</summary>
    public const string Sampling = "sampling";
    /// <summary>Event rejected by a throttling / rate-limit filter.</summary>
    public const string Throttling = "throttling";
    /// <summary>Event rejected by a predicate filter.</summary>
    public const string Predicate = "predicate";
    /// <summary>Event rejected by an unknown plugin filter type.</summary>
    public const string Filter = "filter";
    /// <summary>Event dropped by a redaction rule (e.g. <c>dropEvent when …</c>).</summary>
    public const string Redaction = "redaction";
    /// <summary>Event dropped because the async queue was full (or wait-mode timed out).</summary>
    public const string QueueFull = "queue_full";
    /// <summary>Event dropped because the circuit breaker for the wrapped sink was open.</summary>
    public const string CircuitOpen = "circuit_open";
}

/// <summary>
/// Sentinel implementation for paths where metrics are not wired. Every
/// call is a no-op; callers avoid a null check by using this instead of
/// <c>null</c>.
/// </summary>
public sealed class NullPipelineDropSink : IPipelineDropSink
{
    public static readonly NullPipelineDropSink Instance = new();
    private NullPipelineDropSink() { }
    public void RecordDrop(string? sinkName = null) { }
    public void RecordDrop(string? sinkName, string reason) { }
}
