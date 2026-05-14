#nullable enable

namespace MMP.Herald.Configuration;

/// <summary>
/// Describes what changed between two logging configurations.
/// Used by HotReloadableLoggingBootstrap to decide fast vs slow reload path.
/// </summary>
public sealed record ConfigDiff(
    bool LevelChanged,
    bool RoutesChanged,
    bool SinksChanged,
    bool PresentationChanged,
    bool PipelineStructureChanged,
    SinkChangeKind SinkChange = SinkChangeKind.None)
{
    /// <summary>
    /// True when only the minimum level changed — can be handled by
    /// updating the LogLevelSwitch without rebuilding the pipeline.
    /// </summary>
    public bool IsLevelOnly =>
        LevelChanged && !RoutesChanged && !SinksChanged
        && !PresentationChanged && !PipelineStructureChanged;

    /// <summary>
    /// True when the only structural change is per-sink properties (paths,
    /// URIs, retry policy, property bag) and nothing else — strategy,
    /// routes, presentation, async/batching policy all unchanged. A
    /// future sinks-only rebuild path can use this to reuse the async
    /// queue, WAL, and kernel while replacing the writers underneath.
    /// Currently the bootstrap routes this through the full rebuild,
    /// which is correct (no silent drop) and just heavier than optimal.
    /// </summary>
    public bool IsSinkPropertyOnly =>
        SinkChange == SinkChangeKind.PropertyOnly
        && !LevelChanged
        && !RoutesChanged
        && !PresentationChanged
        && !PipelineStructureChanged;
}
