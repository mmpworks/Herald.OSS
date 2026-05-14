#nullable enable

using System;
using MMP.Herald.Configuration;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Metrics;
using MMP.Herald.Pipeline;

namespace MMP.Herald.Bootstrap;

/// <summary>
/// Result of bootstrapping logging from core configuration.
/// </summary>
public sealed record LoggingBootstrapResult(
    StructuredLogger Logger,
    IAsyncDisposable? AsyncResource,
    ILogLevelRegistry LevelRegistry,
    ILogFailureSink FailureSink,
    DynamicLevelPolicy? DynamicLevelPolicy = null,
    SwappableLogger? SwappableLogger = null,
    HotReloadableLoggingBootstrap? HotReloadBootstrap = null,
    LogMetricsRegistry? MetricsRegistry = null,
    PipelineAccessor? PipelineAccessor = null,
    LogLevel? MinimumLevel = null);
