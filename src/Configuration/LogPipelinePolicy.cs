#nullable enable

using System.Collections.Generic;
using MMP.Herald.Enrichers;
using MMP.Herald.Filters;
using MMP.Herald.Levels;
using MMP.Herald.Metrics;
using MMP.Herald.Pipeline;

namespace MMP.Herald.Configuration;

/// <summary>
/// Top-level policy applied to the logger pipeline.
///
/// <para>
/// <b>DropSink</b> is the metrics hook pipeline decorators call when they
/// discard an event before delivery. The bootstrap populates it from the
/// host's <see cref="LogMetricsRegistry"/>; manual pipeline constructions
/// can leave it null (decorators then record drops against a no-op sink).
/// </para>
/// </summary>
public sealed record LogPipelinePolicy(
    LogLevel MinimumLevel,
    AsyncLogPolicy AsyncPolicy,
    BatchingPolicy BatchingPolicy,
    DynamicLevelPolicy? DynamicLevelPolicy = null,
    // Legacy single sampling-filter slot. Superseded by Filters (the AND-chain list).
    // Kept as a settable parameter so existing call sites + JSON round-trips that fed a
    // single filter still compile; the factory folds it into the Filters list. See the
    // SamplingFilter read-through property below for the obsolete-shim accessor.
    ILogFilter? SamplingFilter = null,
    PostFilteringPolicy? PostFilteringPolicy = null,
    FlightRecorderPolicy? FlightRecorderPolicy = null,
    bool HotReloadEnabled = false,
    IReadOnlyList<ILogEventProcessor>? EventProcessors = null,
    PipelineStrategy? Strategy = null,
    IReadOnlyList<Pipeline.IConfigurablePipelineDecorator>? CustomDecorators = null,
    IPipelineDropSink? DropSink = null,
    // The AND-chained filter list — sampling + throttling + adaptive (and any custom
    // ILogFilter) compose here, each applied independently by the FilteringLogger. This
    // is the supersede of the single SamplingFilter slot; the pipeline factory prefers
    // this list and falls back to the single slot only when this is null (back-compat).
    IReadOnlyList<ILogFilter>? Filters = null,
    // Reconstructed from JsonLoggingConfig.Enrichers by
    // DefaultLoggingConfigurationMapper. JsonConfiguredLoggingBootstrapFactory
    // falls back to this when its enricher constructor arg is null, so a
    // JSON-driven Reload picks up the same enricher chain that the original
    // builder produced. Code-built pipelines still pass an explicit enricher
    // through QuickLogBuilder.Build() at first boot; this field is what
    // makes that survive a hot reload.
    ILogEnricher? Enricher = null)
{
    /// <summary>
    /// The canonical AND-chain filter list the pipeline applies. Prefers <see cref="Filters"/>
    /// (the multi-filter list); when that is null, folds the legacy single
    /// <see cref="SamplingFilter"/> slot into a one-element list; null when neither is set.
    /// This is the obsolete-shim bridge: every consumer reads <c>EffectiveFilters</c>, so the
    /// single-slot callers keep working while the list becomes the source of truth. Removing
    /// the single slot is a separate one-way-door decision deferred to a later release.
    /// </summary>
    public IReadOnlyList<ILogFilter>? EffectiveFilters =>
        Filters is { Count: > 0 } ? Filters
        : SamplingFilter is not null ? new[] { SamplingFilter }
        : null;
}
