#nullable enable

namespace MMP.Herald.Failures;

/// <summary>
/// Why Herald dropped a log event on the way into the pipeline.
///
/// <para>
/// Consumed by drop callbacks on <see cref="Pipeline.AsyncLogger"/> and
/// <see cref="Events.LogEventFactory"/> so operators can tell a legitimate
/// backpressure event apart from a client abuse case. Kept in a single
/// enum so a game loop that pipes the callback into a metric only has
/// one dimension to split.
/// </para>
/// </summary>
public enum DropReason
{
    /// <summary>
    /// The async queue was full and the drop strategy was
    /// <c>drop_write</c> (or an equivalent non-wait mode). A game-loop
    /// producer that spikes past sustained throughput most often lands
    /// here; sustained occurrences mean the downstream pipeline is too
    /// slow and the queue cannot drain.
    /// </summary>
    CapacityFull,

    /// <summary>
    /// The caller was in <c>wait</c> mode and the configured
    /// <c>WaitTimeout</c> expired before the queue had space. Unlike
    /// <see cref="CapacityFull"/>, the write attempt actually blocked
    /// — this reason surfaces when something downstream is stuck, not
    /// just momentarily saturated.
    /// </summary>
    SyncWaitTimeout,

    /// <summary>
    /// The message template or the total size of property values
    /// exceeded the per-event caps on <see cref="Events.LogEventFactory"/>.
    /// A caller passing a 1 GiB template string used to OOM the process
    /// before the filter chain even ran; the size caps turn that into a
    /// dropped-event signal instead.
    /// </summary>
    OversizedEvent,
}
