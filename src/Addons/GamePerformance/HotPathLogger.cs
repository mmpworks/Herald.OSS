#nullable enable

using System;
using System.Runtime.CompilerServices;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Templating;
using MMP.Herald.Time;

namespace MMP.Herald.Addons.GamePerformance;

/// <summary>
/// Bare-bones logger that matches ZLogger's performance profile by skipping
/// enrichment, template parsing, and property resolution entirely.
///
/// Trades features for speed:
/// - No enrichers (no machine name, process ID, trace context)
/// - No message templates (takes pre-formatted strings only)
/// - No properties (EmptyProperties always)
/// - No scoped context (EmptyContext always)
/// - No caller info capture
///
/// What it DOES support:
/// - Level filtering (still goes through FilteringLogger)
/// - All pipeline decorators (async, batching, circuit breaker, etc.)
/// - Multiple sinks via SafeCompositeLogger
/// - Early level guard (zero alloc on rejection)
///
/// Usage:
///   var bare = new HotPathLogger(pipeline, timeProvider, levelRegistry,
///       minimumLevel: KnownLogLevels.Info);
///
///   bare.Info(LogCategory.App, $"Player {name} scored {points}");
///   bare.Warn(LogCategory.Combat, $"Damage overflow: {damage}");
///
/// For the full Herald experience (templates, enrichers, redaction), use StructuredLogger.
/// HotPathLogger is for hot-path game code where every nanosecond matters.
/// </summary>
public sealed class HotPathLogger : MMP.Herald.Pipeline.IComponentMetadata
{
    private readonly ILogger _pipeline;
    private readonly IDateTimeProvider _timeProvider;
    private readonly ILogLevelRegistry? _levelRegistry;
    private readonly LogLevel? _minimumLevel;

    // Sibling StructuredLogger that owns the kernel delegate for this
    // pipeline. When non-null AND StructuredLogger.KernelOrNull is non-null
    // at call time, the accepted path dispatches via a stack-allocated
    // LogEventBuffer and skips the LogEvent allocation + decorator-chain
    // walk. Null falls through to the legacy allocating path, which is
    // the correct behaviour for pipelines with enrichers / async /
    // anything else that fails KernelEligibility.
    //
    // We hold a reference to the StructuredLogger rather than capturing
    // the kernel delegate at construction so that SwapKernel
    // (future hot-reload) is observed consistently. Per-call overhead
    // is one volatile read + one null check — amortised to near-zero
    // when the field is hot in cache.
    private readonly StructuredLogger? _kernelSource;

    /// <summary>
    /// Internal constructor — use QuickLogResult.CreateHotPathLogger() to create.
    /// HotPathLogger is the "HotPath" event creation preset:
    /// pre-formatted strings only, no enrichment, no templates, ~87ns accepted
    /// (or ~10-15ns accepted when the pipeline is kernel-eligible and the
    /// caller passes the sibling StructuredLogger).
    /// </summary>
    // Per-known-level accept booleans, resolved once at construction.
    // Mirrors the pattern on StructuredLogger.IsXxxAcceptable — turns
    // the typed reject path (`hotPath.Info(cat, "string")` with Info
    // below the minimum) into a single field read plus branch. No
    // dictionary lookup, no interface dispatch, no per-call virtual
    // IsAtOrAbove hop. Public so HotPathStringHandler can read them
    // directly via ReferenceEquals on the known level singletons.
    public readonly bool IsTraceAcceptable;
    public readonly bool IsDebugAcceptable;
    public readonly bool IsInfoAcceptable;
    public readonly bool IsWarnAcceptable;
    public readonly bool IsErrorAcceptable;
    public readonly bool IsCriticalAcceptable;

    internal HotPathLogger(
        ILogger pipeline,
        IDateTimeProvider timeProvider,
        ILogLevelRegistry? levelRegistry = null,
        LogLevel? minimumLevel = null,
        StructuredLogger? kernelSource = null) {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _levelRegistry = levelRegistry;
        _minimumLevel = minimumLevel;
        _kernelSource = kernelSource;

        // Precompute per-known-level accept bools. Matches StructuredLogger's
        // pattern: any level not registered in the registry evaluates to
        // false (reject) rather than throwing. IsAtOrAbove throws on
        // unregistered levels, so we probe membership via GetByKeyOrNull
        // first — a registry that only ships Trace..Error (no Critical,
        // a legitimate configuration) returns false for
        // IsCriticalAcceptable instead of throwing at construction.
        if (levelRegistry is not null && minimumLevel is not null)
        {
            IsTraceAcceptable = EvalAccept(levelRegistry, KnownLogLevels.Trace, minimumLevel);
            IsDebugAcceptable = EvalAccept(levelRegistry, KnownLogLevels.Debug, minimumLevel);
            IsInfoAcceptable = EvalAccept(levelRegistry, KnownLogLevels.Info, minimumLevel);
            IsWarnAcceptable = EvalAccept(levelRegistry, KnownLogLevels.Warn, minimumLevel);
            IsErrorAcceptable = EvalAccept(levelRegistry, KnownLogLevels.Error, minimumLevel);
            IsCriticalAcceptable = EvalAccept(levelRegistry, KnownLogLevels.Critical, minimumLevel);
        }
        else
        {
            IsTraceAcceptable = true;
            IsDebugAcceptable = true;
            IsInfoAcceptable = true;
            IsWarnAcceptable = true;
            IsErrorAcceptable = true;
            IsCriticalAcceptable = true;
        }
    }

    private static bool EvalAccept(ILogLevelRegistry registry, LogLevel level, LogLevel minimum) =>
        registry.GetByKeyOrNull(level.Key) is not null
            && registry.IsAtOrAbove(level, minimum);

    // ── String overloads (pre-formatted — for static messages) ────────

    // Typed reject path uses the precomputed IsXxxAcceptable field —
    // one field read + branch. Previously routed through IsEnabled ->
    // _levelRegistry.IsAtOrAbove (interface dispatch + dictionary
    // lookup), measured at ~13 ns; the field read drops it to ~3-4 ns.

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Trace(LogCategory category, string message) {
        if (!IsTraceAcceptable) return;
        LogDirect(KnownLogLevels.Trace, category, message);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Debug(LogCategory category, string message) {
        if (!IsDebugAcceptable) return;
        LogDirect(KnownLogLevels.Debug, category, message);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Info(LogCategory category, string message) {
        if (!IsInfoAcceptable) return;
        LogDirect(KnownLogLevels.Info, category, message);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Warn(LogCategory category, string message) {
        if (!IsWarnAcceptable) return;
        LogDirect(KnownLogLevels.Warn, category, message);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Error(LogCategory category, string message) {
        if (!IsErrorAcceptable) return;
        LogDirect(KnownLogLevels.Error, category, message);
    }

    // ── Interpolated string handler overload (zero-alloc rejection) ──
    //
    // Usage:
    //   bare.Log(category, KnownLogLevels.Info, $"Frame {n}: {ms:F1}ms");
    //
    // The C# compiler transforms the $"..." into a HotPathStringHandler.
    // The handler's constructor receives 'this' and 'level' via
    // InterpolatedStringHandlerArgument, checks IsEnabled, and sets
    // shouldAppend=false if rejected — the compiler skips all Append calls.
    //
    // Rejected: ~13ns, 0 bytes allocated
    // Accepted: ~60-70ns, one string allocation (StringBuilder is pooled)

    /// <summary>
    /// Zero-alloc-rejection log method with interpolated string handler.
    /// The $"..." string is NEVER created if the level is filtered out.
    /// </summary>
    public void Log(LogCategory category, LogLevel level,
        [InterpolatedStringHandlerArgument("", "level")] ref HotPathStringHandler handler)
    {
        if (!handler.IsEnabled) return;
        LogDirect(level, category, handler.ToStringAndReturn());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsEnabled(LogLevel level) {
        // Known-level fast path: the accept bools were precomputed at
        // construction. ReferenceEquals on the singletons collapses to
        // a pointer compare, so the common case (caller passes one of
        // the KnownLogLevels statics) does one load + one branch per
        // level check — same cost as the typed methods. Custom levels
        // fall through to the registry lookup.
        if (ReferenceEquals(level, KnownLogLevels.Info)) return IsInfoAcceptable;
        if (ReferenceEquals(level, KnownLogLevels.Debug)) return IsDebugAcceptable;
        if (ReferenceEquals(level, KnownLogLevels.Warn)) return IsWarnAcceptable;
        if (ReferenceEquals(level, KnownLogLevels.Error)) return IsErrorAcceptable;
        if (ReferenceEquals(level, KnownLogLevels.Trace)) return IsTraceAcceptable;
        if (ReferenceEquals(level, KnownLogLevels.Critical)) return IsCriticalAcceptable;

        if (_levelRegistry is null || _minimumLevel is null) return true;
        return _levelRegistry.IsAtOrAbove(level, _minimumLevel);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LogDirect(LogLevel level, LogCategory category, string message) {
        // Kernel fast path — same shape as StructuredLogger.Log. When the
        // pipeline is kernel-eligible we build a LogEventBuffer on the
        // stack and hand it to the compiled kernel delegate, skipping
        // the heap LogEvent allocation AND the decorator-chain walk.
        // Volatile read via KernelOrNull so a concurrent SwapKernel is
        // observed cleanly by in-flight calls.
        var kernel = _kernelSource?.KernelOrNull;
        if (kernel is not null)
        {
            var buffer = new LogEventBuffer(
                timeUtc: _timeProvider.GetUtcNow(),
                level: level,
                category: category,
                messageTemplate: message,
                message: message,
                properties: ReadOnlySpan<LogProperty>.Empty,
                genSource: _kernelSource?.GenSource);
            kernel(in buffer);
            return;
        }

        // Legacy path — no kernel-eligible pipeline. One LogEvent
        // allocation, plus whatever the decorator chain does downstream.
        var logEvent = new LogEvent(
            TimeUtc: _timeProvider.GetUtcNow(),
            Level: level,
            Category: category,
            MessageTemplate: message,
            Message: message,
            Properties: LogEvent.EmptyProperties,
            Context: LogEvent.EmptyContext,
            GenSource: _kernelSource?.GenSource);

        _pipeline.Log(logEvent);
    }

    // -- IComponentMetadata (single source of truth) --

    internal static readonly HeraldEdition MinEdition = HeraldEdition.Community;
    internal static readonly Configuration.PipelineStepRules StepRules = new(
        OptimalPosition: ["first"],
        IncompatibleWith: ["swappable"],
        MoreInfo: new System.Collections.Generic.Dictionary<string, string> {
            ["optimalPosition"] = "HotPath is a pipeline entry point. It must be at position 1.",
            ["incompatibleWith"] = "HotPath and HotReload are mutually exclusive entry points. HotPath trades hot-reload for maximum speed (~14ns rejection)."
        });

    string Pipeline.IComponentMetadata.ComponentName => "hotPath";
    string Pipeline.IComponentMetadata.DisplayName => "Entry Point: HotPath";
    string Pipeline.IComponentMetadata.Description => "Bare-bones event creation: pre-formatted strings, no enrichment, ~14ns rejection.";
    string Pipeline.IComponentMetadata.Help => "The HotPath entry point skips enrichment, template parsing, properties, scoped context. Takes pre-formatted strings only. All methods [AggressiveInlining]. Use for game loop inner ticks where sub-100ns matters. HotPath inherits the pipeline's minimum level for zero-alloc rejection.";
    Pipeline.VendorInfo Pipeline.IComponentMetadata.Vendor => Pipeline.VendorInfo.MMP;
    System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> Pipeline.IComponentMetadata.ConfigurationSchema => [];
    Configuration.PipelineStepRules Pipeline.IComponentMetadata.Rules => StepRules;
    HeraldEdition Pipeline.IComponentMetadata.MinimumEdition => MinEdition;
}
