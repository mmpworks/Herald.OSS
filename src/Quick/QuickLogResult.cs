#nullable enable

using MMP.Herald.Bootstrap;
using MMP.Herald.Configuration;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Metrics;
using MMP.Herald.Pipeline;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Spans;
using MMP.Herald.Time;

namespace MMP.Herald.Quick;

/// <summary>
/// Result of QuickLogBuilder.Build().
/// Provides the standard logger, factories, and runtime access for
/// health monitoring, configuration management, and diagnostics.
/// </summary>
public sealed class QuickLogResult : System.IAsyncDisposable
{
    private readonly LoggingBootstrapResult _bootstrapResult;
    private readonly IDateTimeProvider _timeProvider;

    public QuickLogResult(
        StructuredLogger logger,
        LoggingBootstrapResult bootstrapResult,
        IDateTimeProvider timeProvider,
        PipelineAccessor? pipelineAccessor = null,
        string? pipelineName = null) {
        Logger = logger;
        _bootstrapResult = bootstrapResult;
        _timeProvider = timeProvider;
        Pipeline = pipelineAccessor ?? new PipelineAccessor();
        PipelineName = pipelineName;
    }

    // -- Core --

    /// <summary>The standard structured logger.</summary>
    public StructuredLogger Logger { get; }

    /// <summary>
    /// The name the builder registered this pipeline under, or null
    /// when the pipeline was built without a name. Same value the
    /// builder's <c>RegistryName</c> carries; surfacing it here so a
    /// caller can discover the name from the result without holding
    /// the builder reference. Multi-pipeline hosts use this to label
    /// per-pipeline subscribers (Dashboard's LiveLogCapture, audit
    /// sinks, etc.) without Core having to thread pipeline-name into
    /// its broadcaster surfaces.
    /// </summary>
    public string? PipelineName { get; }

    /// <summary>
    /// Typed access to pipeline components. Use to reach decorators at runtime:
    ///
    ///   result.Pipeline.Get&lt;AsyncLogger&gt;()?.QueueDepth;           // null if absent
    ///   result.Pipeline.Require&lt;CircuitBreakerLogger&gt;();          // throws if absent
    ///   result.Pipeline.GetOrDefault(fallbackLogger);              // caller-supplied default
    /// </summary>
    public PipelineAccessor Pipeline { get; }

    // -- Factories --

    /// <summary>Create an ActivityWriter bound to a named channel.</summary>
    public ActivityWriter CreateActivityWriter(string channel) =>
        new(Logger, channel);

    /// <summary>Create a span factory for structured span lifecycle tracking.</summary>
    public LogSpanFactory CreateSpanFactory(ISpanMetricsCollector? metricsCollector = null) =>
        new(Logger, _timeProvider, metricsCollector);

    // -- Runtime access --

    /// <summary>
    /// Level registry. Query available levels, check ranks, compare.
    /// After a hot reload changes the level ordering (e.g. the
    /// dashboard's drag-rearrange path through <c>SetLevelOrder</c>),
    /// this property returns the post-reload registry so consumers see
    /// the new ranks. Falls back to the registry built at construction
    /// if hot reload is disabled or has not run yet.
    /// </summary>
    public ILogLevelRegistry LevelRegistry =>
        _bootstrapResult.HotReloadBootstrap?.CurrentLevelRegistry
        ?? _bootstrapResult.LevelRegistry;

    /// <summary>Failure sink. Cast to DiagnosticLogFailureSink to inspect recent failures.</summary>
    public ILogFailureSink FailureSink => _bootstrapResult.FailureSink;

    /// <summary>Metrics registry. Per-sink delivery counts, failures, latency. Null if not enabled.</summary>
    public LogMetricsRegistry? MetricsRegistry => _bootstrapResult.MetricsRegistry;

    /// <summary>Hot-reload bootstrap. SwitchConfigFile(), Reload(), WatchFile(). Null if not enabled.</summary>
    public HotReloadableLoggingBootstrap? HotReloadBootstrap => _bootstrapResult.HotReloadBootstrap;

    /// <summary>
    /// Dynamic level policy. Change minimum level at runtime via GlobalLevelSwitch.
    /// After a hot reload the pipeline rebuilds its global switch, so this
    /// property delegates to <see cref="HotReloadableLoggingBootstrap.CurrentDynamicLevelPolicy"/>
    /// and falls back to the build-time snapshot when no reload has run yet.
    /// </summary>
    public DynamicLevelPolicy? DynamicLevelPolicy =>
        _bootstrapResult.HotReloadBootstrap?.CurrentDynamicLevelPolicy
        ?? _bootstrapResult.DynamicLevelPolicy;

    /// <summary>
    /// Pipeline minimum level currently in effect. After
    /// <c>SetMinimumLevel</c> triggers a reload, this returns the live
    /// floor — the same one the runtime filter checks. Falls back to the
    /// build-time minimum when no reload has run yet, so non-hot-reload
    /// callers see no behaviour change. Null only when neither side of
    /// the chain has set a minimum (e.g. test pipelines built without
    /// going through the regular bootstrap).
    /// </summary>
    public Levels.LogLevel? MinimumLevel =>
        _bootstrapResult.HotReloadBootstrap?.CurrentMinimumLevel
        ?? _bootstrapResult.MinimumLevel;

    /// <summary>Async resource. Call DisposeAsync() to flush before process exit.</summary>
    public System.IAsyncDisposable? AsyncResource => _bootstrapResult.AsyncResource;

    /// <summary>
    /// Kernel-path diagnostic snapshot taken at pipeline construction.
    /// Reports whether the kernel fast path activated and, if not, the
    /// human-readable reason it was rejected from
    /// <see cref="KernelEligibility.DescribeRejection"/>. Every built-in
    /// Herald.OSS sink implements <see cref="IKernelSink"/>, so a default
    /// pipeline reports <see cref="KernelDiagnostic.KernelEligible"/> =
    /// <c>true</c>; custom sinks that skip the interface drop the pipeline
    /// to the chain path and surface their type name in
    /// <see cref="KernelDiagnostic.RejectionReason"/>. Null when the
    /// pipeline was not built through
    /// <see cref="DefaultLogPipelineFactory"/>.
    /// </summary>
    public KernelDiagnostic? KernelDiagnostic => _bootstrapResult.KernelDiagnostic;

    // -- HotPathLogger factory --

    /// <summary>
    /// Create a HotPathLogger for game loop hot paths.
    /// Uses the same pipeline and sinks as the StructuredLogger but skips
    /// enrichment, template parsing, and property resolution.
    /// Use string interpolation: bare.Info(category, $"Frame {n}: {ms}ms")
    /// </summary>
    /// <summary>
    /// Create a logger using the specified event creation preset.
    /// All presets share the same decorator chain and sinks — they differ
    /// only in how LogEvent instances are created.
    ///
    ///   Structured: full template parsing, enrichment, caller info (~576-1,091ns)
    ///   HotPath:    pre-formatted strings, no enrichment (~87ns)
    /// </summary>
    public object CreateLogger(MMP.Herald.Pipeline.EventCreationPreset preset,
        Levels.LogLevel? minimumLevel = null) => preset switch
    {
        MMP.Herald.Pipeline.EventCreationPreset.HotPath => CreateHotPathLogger(minimumLevel),
        MMP.Herald.Pipeline.EventCreationPreset.Structured => Logger,
        _ => Logger
    };

    /// <summary>
    /// Create a HotPath preset logger for game loop hot paths.
    /// Routes through the full decorator chain (async, filtering, sinks).
    /// Use for hot paths that still need pipeline features like async offloading.
    /// ~130ns per accepted event (BDN).
    /// </summary>
    public Addons.GamePerformance.HotPathLogger CreateHotPathLogger(
        Levels.LogLevel? minimumLevel = null) =>
        new(_bootstrapResult.SwappableLogger?.Current ?? Logger.Pipeline,
            _timeProvider, LevelRegistry,
            minimumLevel ?? MinimumLevel,
            // Sibling StructuredLogger — HotPathLogger reads
            // KernelOrNull from it per-call to pick the kernel
            // fast path when the pipeline is kernel-eligible.
            kernelSource: Logger);

    /// <summary>
    /// Create a direct HotPath logger that bypasses ALL pipeline decorators.
    /// Events go straight from HotPathLogger → first sink. No async, no filtering,
    /// no batching, no circuit breaker. Maximum speed: ~24ns per event.
    ///
    /// Use ONLY when:
    /// - Every nanosecond matters (inner physics/render loop)
    /// - You don't need async offloading (sink I/O is fast, e.g., in-memory)
    /// - You handle level filtering yourself (HotPathLogger has its own IsEnabled)
    /// - You accept that hot reload won't affect this logger
    ///
    /// For most game loops, CreateHotPathLogger() (~130ns) is fast enough.
    /// Use this only when profiling proves the pipeline overhead matters.
    /// </summary>
    public Addons.GamePerformance.HotPathLogger CreateDirectHotPathLogger(
        Levels.LogLevel? minimumLevel = null)
    {
        // Find the terminal sink — bypass the entire decorator chain.
        // If SafeCompositeLogger exists, use it directly (still fan-out to all sinks).
        // Otherwise, use whatever is at the bottom of the chain.
        var terminal = Pipeline.Get<Pipeline.SafeCompositeLogger>() as ILogger
            ?? _bootstrapResult.SwappableLogger?.Current
            ?? Logger.Pipeline;

        return new Addons.GamePerformance.HotPathLogger(
            terminal, _timeProvider, LevelRegistry,
            minimumLevel ?? MinimumLevel,
            // Even the "direct" HotPath logger benefits from the kernel
            // when the pipeline is eligible — the kernel delegate
            // encodes the full fan-out, so using it still skips the
            // decorator chain AND the LogEvent allocation.
            kernelSource: Logger);
    }

    // -- Build / Commit --

    /// <summary>
    /// Create a QuickLogResult from a PipelineBuildResult.
    /// Used by BuildAndCommit() for first-time pipeline creation.
    /// </summary>
    public static QuickLogResult FromBuild(PipelineBuildResult buildResult)
    {
        System.ArgumentNullException.ThrowIfNull(buildResult);
        return new QuickLogResult(
            buildResult.Logger, buildResult.BootstrapResult,
            buildResult.TimeProvider, buildResult.PipelineAccessor,
            pipelineName: buildResult.PipelineName);
    }

    /// <summary>
    /// Commit a previously built pipeline, making it live via SwappableLogger.
    /// The PipelineBuildResult was produced by QuickLogBuilder.Build() and may
    /// have been inspected/validated before this call.
    ///
    /// Returns true if the commit succeeded (hot reload available),
    /// false if the pipeline cannot be swapped.
    ///
    /// Usage:
    ///   var proposal = builder.Build();
    ///   // inspect proposal.ExportConfig() ...
    ///   result.Commit(proposal);  // atomic swap
    /// </summary>
    public bool Commit(PipelineBuildResult buildResult)
    {
        System.ArgumentNullException.ThrowIfNull(buildResult);

        if (_bootstrapResult.HotReloadBootstrap is null)
            return false;

        _bootstrapResult.HotReloadBootstrap.Reload(buildResult.ConfigJson);
        return true;
    }

    /// <summary>
    /// Rebuild the pipeline from the given builder's current state and swap it
    /// into the live pipeline. Convenience for Build() + Commit().
    ///
    /// Returns true if the rebuild succeeded, false if hot reload is not available.
    /// </summary>
    public bool RebuildFrom(QuickLogBuilder builder)
    {
        System.ArgumentNullException.ThrowIfNull(builder);
        var buildResult = builder.Build();
        return Commit(buildResult);
    }

    // -- Disposal --

    /// <summary>
    /// Flush buffered events and release async resources.
    /// Enables <c>await using var result = builder.Build();</c>
    /// </summary>
    public async System.Threading.Tasks.ValueTask DisposeAsync()
    {
        if (_bootstrapResult.AsyncResource is not null)
            await _bootstrapResult.AsyncResource.DisposeAsync().ConfigureAwait(false);
    }

    // -- Diagnostics --

    /// <summary>
    /// Returns a multi-line diagnostic string describing the active pipeline.
    /// Paste into support tickets, log at startup, or display in dev console.
    /// </summary>
    public string DiagnosticDump() {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Herald Pipeline Diagnostic ===");

        // Walk the pipeline chain via IDescribable
        sb.AppendLine("Pipeline:");
        sb.AppendLine("  StructuredLogger");
        DescribeChain(sb, _bootstrapResult.SwappableLogger);

        // Level registry
        sb.AppendLine($"Minimum Level: {(MinimumLevel?.DisplayName ?? "not set")}");

        // Failure sink
        if (_bootstrapResult.FailureSink is DiagnosticLogFailureSink diag)
        {
            sb.AppendLine($"Failure Sink: DiagnosticLogFailureSink ({diag.GetEntries().Count} recorded failures)");
        }

        // Hot reload
        sb.AppendLine($"Hot Reload: {(_bootstrapResult.HotReloadBootstrap is not null ? "enabled" : "disabled")}");

        sb.AppendLine("=================================");
        return sb.ToString();
    }

    private static void DescribeChain(System.Text.StringBuilder sb, ILogger? logger) {
        var current = logger;
        var depth = 1;

        while (current is not null)
        {
            var indent = new string(' ', (depth + 1) * 2);

            if (current is IDescribable describable)
            {
                sb.AppendLine($"{indent}-> {describable.Describe()}");
            }
            else
            {
                sb.AppendLine($"{indent}-> {current.GetType().Name}");
            }

            // Try to follow the chain via reflection-free known patterns
            current = current switch
            {
                SwappableLogger sw => sw.Current,
                _ => null // can't follow further without reflection
            };
            depth++;
        }
    }
}
