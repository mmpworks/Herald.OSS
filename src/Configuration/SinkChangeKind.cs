#nullable enable

namespace MMP.Herald.Configuration;

/// <summary>
/// Granularity of the sink-set change between two
/// <see cref="Runtime.LoggingRuntimeConfiguration"/> snapshots.
///
/// <para>
/// Pre-fix the diff only compared <c>(Name, Kind)</c> pairs, so a config
/// edit that changed an existing sink's <c>path</c>, <c>uri</c>, or any
/// property bag value reported <see cref="None"/> and the level-only fast
/// path applied without rebuilding the sink. Post-fix, the kind makes the
/// distinction explicit:
/// </para>
///
/// <list type="bullet">
///   <item><see cref="None"/>: sinks identical.</item>
///   <item><see cref="PropertyOnly"/>: same set of <c>(Name, Kind)</c>
///         pairs, but at least one per-sink field differs (path, uri,
///         properties bag, retry policy, run state, …). The bootstrap must
///         rebuild the sink writer; the async queue and WAL can be reused
///         in a future minimal-rebuild path.</item>
///   <item><see cref="Structural"/>: a sink was added, removed, or had its
///         <c>Kind</c> changed. The pipeline shape is different; a full
///         rebuild is required.</item>
/// </list>
/// </summary>
public enum SinkChangeKind
{
    None = 0,
    PropertyOnly = 1,
    Structural = 2,
}
