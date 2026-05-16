#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Metrics;
using MMP.Herald.Pipeline;
using MMP.Herald.Pipeline.Kernel;

namespace MMP.Herald.Bootstrap;

/// <summary>
/// Result of bootstrapping logging from core configuration.
///
/// <para>
/// <see cref="AsyncResource"/> holds the async-disposable wrapper
/// (drain-then-release semantics — <see cref="AsyncLogger"/>,
/// <see cref="BatchingLogger"/>, etc.). <see cref="SyncResources"/>
/// holds any sync <see cref="IDisposable"/> sinks tracked by the
/// pipeline assembly — file sinks and other handle-owning sinks
/// that need a plain <see cref="IDisposable.Dispose"/> call to
/// release. <see cref="QuickLogResult.DisposeAsync"/> walks both
/// phases in order.
/// </para>
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
    LogLevel? MinimumLevel = null,
    KernelDiagnostic? KernelDiagnostic = null,
    IReadOnlyList<IDisposable>? SyncResources = null);
