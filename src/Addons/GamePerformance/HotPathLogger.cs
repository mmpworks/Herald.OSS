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
///       minimumLevel: KnownLogLevels.Information);
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
    // Per-known-level accept booleans, resolved at construction and
    // updateable via RecomputeAcceptables on a level-only hot reload.
    // Mirrors the pattern on StructuredLogger.IsXxxAcceptable — turns
    // the typed reject path (`hotPath.Info(cat, "string")` with Info
    // below the minimum) into a single Volatile.Read plus branch. No
    // dictionary lookup, no interface dispatch, no per-call virtual
    // IsAtOrAbove hop. Public so HotPathStringHandler can read them
    // directly via ReferenceEquals on the known level singletons.
    private bool _isVerboseAcceptable;
    private bool _isDebugAcceptable;
    private bool _isInformationAcceptable;
    private bool _isWarningAcceptable;
    private bool _isErrorAcceptable;
    private bool _isFatalAcceptable;

    public bool IsVerboseAcceptable    => System.Threading.Volatile.Read(ref _isVerboseAcceptable);
    public bool IsDebugAcceptable    => System.Threading.Volatile.Read(ref _isDebugAcceptable);
    public bool IsInformationAcceptable     => System.Threading.Volatile.Read(ref _isInformationAcceptable);
    public bool IsWarningAcceptable     => System.Threading.Volatile.Read(ref _isWarningAcceptable);
    public bool IsErrorAcceptable    => System.Threading.Volatile.Read(ref _isErrorAcceptable);
    public bool IsFatalAcceptable => System.Threading.Volatile.Read(ref _isFatalAcceptable);

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
        // IsFatalAcceptable instead of throwing at construction.
        if (levelRegistry is not null && minimumLevel is not null)
        {
            _isVerboseAcceptable    = EvalAccept(levelRegistry, KnownLogLevels.Verbose, minimumLevel);
            _isDebugAcceptable    = EvalAccept(levelRegistry, KnownLogLevels.Debug, minimumLevel);
            _isInformationAcceptable     = EvalAccept(levelRegistry, KnownLogLevels.Information, minimumLevel);
            _isWarningAcceptable     = EvalAccept(levelRegistry, KnownLogLevels.Warning, minimumLevel);
            _isErrorAcceptable    = EvalAccept(levelRegistry, KnownLogLevels.Error, minimumLevel);
            _isFatalAcceptable = EvalAccept(levelRegistry, KnownLogLevels.Fatal, minimumLevel);
        }
        else
        {
            _isVerboseAcceptable    = true;
            _isDebugAcceptable    = true;
            _isInformationAcceptable     = true;
            _isWarningAcceptable     = true;
            _isErrorAcceptable    = true;
            _isFatalAcceptable = true;
        }
    }

    /// <summary>
    /// Recompute the per-known-level accept booleans against the supplied
    /// minimum level and atomically publish them. Mirrors
    /// <c>StructuredLogger.RecomputeAcceptables</c> for the HotPath preset
    /// so a level-only hot reload reaches both presets.
    /// </summary>
    internal void RecomputeAcceptables(LogLevel? newMinimumLevel)
    {
        if (_levelRegistry is null || newMinimumLevel is null)
        {
            System.Threading.Volatile.Write(ref _isVerboseAcceptable, true);
            System.Threading.Volatile.Write(ref _isDebugAcceptable, true);
            System.Threading.Volatile.Write(ref _isInformationAcceptable, true);
            System.Threading.Volatile.Write(ref _isWarningAcceptable, true);
            System.Threading.Volatile.Write(ref _isErrorAcceptable, true);
            System.Threading.Volatile.Write(ref _isFatalAcceptable, true);
            return;
        }

        System.Threading.Volatile.Write(ref _isVerboseAcceptable,    EvalAccept(_levelRegistry, KnownLogLevels.Verbose, newMinimumLevel));
        System.Threading.Volatile.Write(ref _isDebugAcceptable,    EvalAccept(_levelRegistry, KnownLogLevels.Debug, newMinimumLevel));
        System.Threading.Volatile.Write(ref _isInformationAcceptable,     EvalAccept(_levelRegistry, KnownLogLevels.Information, newMinimumLevel));
        System.Threading.Volatile.Write(ref _isWarningAcceptable,     EvalAccept(_levelRegistry, KnownLogLevels.Warning, newMinimumLevel));
        System.Threading.Volatile.Write(ref _isErrorAcceptable,    EvalAccept(_levelRegistry, KnownLogLevels.Error, newMinimumLevel));
        System.Threading.Volatile.Write(ref _isFatalAcceptable, EvalAccept(_levelRegistry, KnownLogLevels.Fatal, newMinimumLevel));
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
    public void Verbose(LogCategory category, string message) {
        if (!IsVerboseAcceptable) return;
        LogDirect(KnownLogLevels.Verbose, category, message);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Debug(LogCategory category, string message) {
        if (!IsDebugAcceptable) return;
        LogDirect(KnownLogLevels.Debug, category, message);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Information(LogCategory category, string message) {
        if (!IsInformationAcceptable) return;
        LogDirect(KnownLogLevels.Information, category, message);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Warning(LogCategory category, string message) {
        if (!IsWarningAcceptable) return;
        LogDirect(KnownLogLevels.Warning, category, message);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Error(LogCategory category, string message) {
        if (!IsErrorAcceptable) return;
        LogDirect(KnownLogLevels.Error, category, message);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Fatal(LogCategory category, string message) {
        if (!IsFatalAcceptable) return;
        LogDirect(KnownLogLevels.Fatal, category, message);
    }

    // ── Interpolated string handler overload (zero-alloc rejection) ──
    //
    // Usage:
    //   bare.Log(category, KnownLogLevels.Information, $"Frame {n}: {ms:F1}ms");
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
        if (ReferenceEquals(level, KnownLogLevels.Information)) return IsInformationAcceptable;
        if (ReferenceEquals(level, KnownLogLevels.Debug)) return IsDebugAcceptable;
        if (ReferenceEquals(level, KnownLogLevels.Warning)) return IsWarningAcceptable;
        if (ReferenceEquals(level, KnownLogLevels.Error)) return IsErrorAcceptable;
        if (ReferenceEquals(level, KnownLogLevels.Verbose)) return IsVerboseAcceptable;
        if (ReferenceEquals(level, KnownLogLevels.Fatal)) return IsFatalAcceptable;

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
                properties: ReadOnlySpan<LogProperty>.Empty);
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
            Context: LogEvent.EmptyContext);

        _pipeline.Log(logEvent);
    }

    // -- IComponentMetadata (single source of truth) --

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
}
