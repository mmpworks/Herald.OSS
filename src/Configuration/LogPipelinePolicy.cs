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
    ILogFilter? SamplingFilter = null,
    PostFilteringPolicy? PostFilteringPolicy = null,
    FlightRecorderPolicy? FlightRecorderPolicy = null,
    bool HotReloadEnabled = false,
    IReadOnlyList<ILogEventProcessor>? EventProcessors = null,
    PipelineStrategy? Strategy = null,
    IReadOnlyList<Pipeline.IConfigurablePipelineDecorator>? CustomDecorators = null,
    IPipelineDropSink? DropSink = null,
    // Reconstructed from JsonLoggingConfig.Enrichers by
    // DefaultLoggingConfigurationMapper. JsonConfiguredLoggingBootstrapFactory
    // falls back to this when its enricher constructor arg is null, so a
    // JSON-driven Reload picks up the same enricher chain that the original
    // builder produced. Code-built pipelines still pass an explicit enricher
    // through QuickLogBuilder.Build() at first boot; this field is what
    // makes that survive a hot reload.
    ILogEnricher? Enricher = null);
