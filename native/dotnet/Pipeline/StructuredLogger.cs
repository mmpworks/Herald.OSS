#nullable enable

using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using MMP.Herald.Diagnostics;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Templating;
using MMP.Herald.Templating.NamingPolicies;
using MMP.Herald.Time;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Friendly application-facing logger.
/// Supports plain messages, message templates with structured properties, scoped context,
/// exception capture, and optional caller information.
/// </summary>
public sealed partial class StructuredLogger : ILogger, IComponentMetadata, IInternalLogForwarder
{
    private readonly ILogger _pipeline;
    private readonly ILogEventFactory _eventFactory;
    private readonly ILogScopeProvider _scopeProvider;
    private readonly IReadOnlyDictionary<string, object?> _defaultContext;
    private readonly bool _includeCallerInfo;
    private readonly ILogLevelRegistry? _levelRegistry;
    private readonly LogLevel? _minimumLevel;

    // Reject-path cache: the minimum level's rank, resolved once at
    // construction. When <c>&gt;= 0</c>, <see cref="IsEnabled"/> can skip
    // the minimum-level dictionary lookup entirely and compare ranks
    // directly. When <c>-1</c>, either the registry or the minimum level
    // is null and every call is accepted.
    private readonly int _minimumLevelRank = -1;

    // Direct rank-by-key map captured at construction. Replaces the
    // virtual <c>_levelRegistry.GetRank(level)</c> dispatch on the reject
    // path with a FrozenDictionary lookup keyed by the level's <c>Key</c>
    // string. FrozenDictionary is optimised for read-only small maps
    // (the level set is typically 6-10 entries) and outperforms a
    // regular <c>Dictionary&lt;string,int&gt;</c> lookup by ~30-40% for
    // this size class. The interface-dispatch saving compounds.
    private readonly FrozenDictionary<string, int>? _rankByKey;

    // Per-known-level precomputed accept booleans. The interpolated
    // string handlers (DebugLogInterpolatedStringHandler etc.) read these
    // directly instead of calling IsEnabled(level) — turning the reject
    // path into a single bool-field read plus branch, no dictionary
    // lookup, no arg-null check, no interface dispatch.
    //
    // Exposed as properties (not fields) so a level-only hot reload can
    // recompute them in place against the new minimum. The fields used
    // to be readonly bools, which left the post-reload accept state
    // pinned to the construction-time minimum — source-gen-emitted code
    // reading `logger.IsDebugAcceptable` kept dropping events when the
    // minimum was lowered. The property getter is a single
    // Volatile.Read, so the emitted reject path is still one load plus
    // branch.
    private bool _isVerboseAcceptable;
    private bool _isDebugAcceptable;
    private bool _isInformationAcceptable;
    private bool _isWarningAcceptable;
    private bool _isErrorAcceptable;
    private bool _isFatalAcceptable;

    public bool IsVerboseAcceptable     => System.Threading.Volatile.Read(ref _isVerboseAcceptable);
    public bool IsDebugAcceptable       => System.Threading.Volatile.Read(ref _isDebugAcceptable);
    public bool IsInformationAcceptable => System.Threading.Volatile.Read(ref _isInformationAcceptable);
    public bool IsWarningAcceptable     => System.Threading.Volatile.Read(ref _isWarningAcceptable);
    public bool IsErrorAcceptable       => System.Threading.Volatile.Read(ref _isErrorAcceptable);
    public bool IsFatalAcceptable       => System.Threading.Volatile.Read(ref _isFatalAcceptable);

    // Kernel fast path — when set, the core Log(...) method can bypass event
    // factory construction and chain traversal for the common case (no
    // context, no eventId, no caller info, no exception). Set at bootstrap
    // only when the whole pipeline passes KernelEligibility. Null falls
    // through to the decorator chain, preserving today's behavior.
    //
    // The kernel lives behind a KernelHolder rather than a direct field so
    // every child logger produced by ForContext shares the same indirection
    // with its parent. A hot-reload SwapKernel call on the parent is observed
    // by long-running scope-bearing children (per-request ASP.NET loggers,
    // typically) on their next dispatch — without the holder, the child
    // captured the parent's kernel by value at ForContext time and silently
    // dispatched through the orphaned old kernel after a reload.
    private readonly KernelHolder _kernelHolder;
    private readonly IDateTimeProvider? _dateTimeProvider;

    // Optional kernel-aware redactor that runs on the property span before
    // the LogEventBuffer is constructed. Null on the no-redaction path —
    // every kernel dispatch site reads it via a single null check; when null,
    // the JIT folds the conditional to nothing. When non-null, the redactor's
    // Apply is allocation-free on no-match and allocates a single rewritten
    // array on first match. See FastPathRedactor for the design.
    //
    // Atomically swappable like _kernel so a future hot-reload integration
    // can install / clear the redactor in lockstep with kernel rebuilds.
    // Volatile reads on the hot path keep that contract honest.
    private FastPathRedactor? _fastRedactor;

    // Optional kernel-aware sampler. Same shape as the redactor: null on
    // the no-sampling path; volatile read at every dispatch site folds out
    // when not configured. When non-null, ShouldEmit returns false for
    // events the sampler decides to drop — the dispatch site short-circuits
    // before any LogEventBuffer construction happens, so dropped events
    // pay only one Interlocked.Increment and one modulo. Runs BEFORE the
    // redactor (no point redacting events that are about to be dropped).
    private FastPathSampler? _fastSampler;

    // Optional kernel-aware static enricher. Runs AFTER the redactor in
    // the dispatch order — appended properties are by construction
    // operator-supplied (host.name, service.name, etc.) and not subject
    // to the redaction rule set; running afterward avoids walking the
    // appended slots a second time. One allocation per accepted event
    // when configured: a new property array sized input + Count.
    private FastPathEnricher? _fastEnricher;

    // Optional kernel-aware dynamic level resolver. Reads from a
    // LogLevelSwitch whose level can be mutated at runtime. Runs FIRST
    // among the dispatch-site checks — cheaper to reject on level
    // before incrementing the sampler counter or walking the redactor
    // rule set. Null on the no-dynamic-level path; the JIT folds the
    // null-check away under inlining.
    private FastPathDynamicLevel? _fastDynamicLevel;

    // Property-naming policy applied by the typed-args runtime dispatch path
    // (Phase 4 source-gen wires the generated DispatchTypedN through this
    // logger's ResolveNames helper). When null, the logger treats the default
    // as PascalCasePolicy.Instance — null lets the multi-policy interceptor
    // (P3) short-circuit through the Pascal-baked literal arm with no extra
    // resolve work. Mutable to support post-bootstrap install via
    // QuickLogBuilder (matching the FastPath install pattern) — access goes
    // through Volatile.Read / Interlocked.Exchange. Snapshotted by
    // ForContext-derived child loggers — a parent rebuilt with a different
    // policy does not retroactively flip the child.
    private IPropertyNamingPolicy? _namingPolicy;

    // Companion to _namingPolicy: the BuiltinPolicy kind the active policy
    // maps to (Pascal/Snake/Camel/Custom). The multi-policy interceptor
    // dispatches on this via Volatile.Read + a 3-arm switch; the runtime
    // cache continues to use the policy reference for identity.
    //
    // Kept in lockstep with _namingPolicy by InstallNamingPolicy: null or
    // PascalCasePolicy -> Pascal; SnakeCasePolicy -> Snake; CamelCasePolicy
    // -> Camel; anything else -> Custom. Custom forces the interceptor to
    // fall through to the runtime resolver path.
    private BuiltinPolicy _currentPolicyKind;

    // Optimization: cache last caller file name to avoid repeated Path.GetFileName calls.
    // Most consecutive events come from the same file, so this hits ~90%+ of the time.
    private string? _lastCallerFileFull;
    private string? _lastCallerFileName;

    // External-event-injection consent. Build-time decision set once by
    // QuickLogBuilder.AllowExternalEventInjection() and carried here through
    // the JSON config (see docs/design/external-event-injection-switch.md).
    // When false, the public Log(LogEvent) port — the ONLY external injection
    // boundary — drops the event and fires a one-shot loud notice. The typed
    // surface (Info/Warn/core Log(level,...)) never reaches that port: it builds
    // via the factory and forwards to _pipeline directly, so internal events
    // cannot trip the consent gate by construction (ADR section 7.1 entry-point
    // discrimination). Immutable + propagated through ForContext so a child
    // logger inherits the parent's consent.
    private readonly bool _allowExternalEventInjection;

    // One-shot latch for the OFF-path refusal notice. 0 = not yet fired,
    // 1 = fired. Interlocked.Exchange flips it so the un-silenceable notice
    // surfaces on the FIRST off-path injection and never spams thereafter
    // (ADR section 4.1 "fires on the FIRST such call"). Per-logger, matching
    // the naming-policy announcement's one-shot shape. Access only via
    // Interlocked; intentionally non-volatile — Interlocked provides the barrier.
    private int _externalInjectionNoticeFired;

    // The host-scoped runtime-message channel this logger publishes its own
    // framework notices to — the naming-policy announcement (Naming.cs) and the
    // injection-refusal notice (Injection.cs). Defaults to
    // HeraldHost.Default.RuntimeMessages so every existing caller behaves exactly
    // as before; the build site supplies a non-default host's channel when one is
    // configured. Readonly + propagated by reference through ForContext (same
    // discipline as _kernelHolder) so a child logger publishes to its parent's
    // host channel and never silently falls back to Default — that fallback would
    // re-open the cross-host buffer bleed this field exists to close. See
    // docs/design/announcement-channel-per-host-routing.md.
    private readonly HeraldRuntimeMessagesInstance _runtimeMessages;

    /// <summary>
    /// The inner pipeline. Used by CreateHotPathLogger() to bypass StructuredLogger's
    /// template parsing and enrichment while sharing the same sink chain.
    /// </summary>
    internal ILogger Pipeline => _pipeline;

    /// <summary>
    /// Internal constructor — use QuickLogBuilder.Build() to create.
    /// StructuredLogger is the "Structured" event creation preset:
    /// full template parsing, enrichment, caller info, scoped context.
    /// </summary>
    internal StructuredLogger(
        ILogger pipeline,
        ILogEventFactory eventFactory,
        ILogScopeProvider scopeProvider,
        IReadOnlyDictionary<string, object?>? defaultContext = null,
        bool includeCallerInfo = false,
        ILogLevelRegistry? levelRegistry = null,
        LogLevel? minimumLevel = null,
        LogKernel? kernel = null,
        IDateTimeProvider? dateTimeProvider = null,
        IPropertyNamingPolicy? namingPolicy = null,
        bool allowExternalEventInjection = false,
        // Null default (not HeraldHost.Default.RuntimeMessages) because a default
        // parameter value must be a compile-time constant; the private ctor
        // coalesces null to the default host's channel. This keeps every existing
        // caller — which never passes this argument — on Default, unchanged.
        HeraldRuntimeMessagesInstance? runtimeMessages = null)
        : this(pipeline, eventFactory, scopeProvider, defaultContext, includeCallerInfo,
            levelRegistry, minimumLevel, new KernelHolder(kernel), dateTimeProvider, namingPolicy,
            allowExternalEventInjection, runtimeMessages)
    {
    }

    // Private constructor that shares an existing KernelHolder with the
    // caller (used by ForContext) so the child observes parent SwapKernel
    // calls. The public-style constructor above wraps a fresh holder around
    // the supplied kernel.
    private StructuredLogger(
        ILogger pipeline,
        ILogEventFactory eventFactory,
        ILogScopeProvider scopeProvider,
        IReadOnlyDictionary<string, object?>? defaultContext,
        bool includeCallerInfo,
        ILogLevelRegistry? levelRegistry,
        LogLevel? minimumLevel,
        KernelHolder kernelHolder,
        IDateTimeProvider? dateTimeProvider,
        IPropertyNamingPolicy? namingPolicy,
        bool allowExternalEventInjection,
        HeraldRuntimeMessagesInstance? runtimeMessages)
    {
        _pipeline = pipeline;
        _eventFactory = eventFactory;
        _scopeProvider = scopeProvider;
        _defaultContext = defaultContext ?? LogEvent.EmptyContext;
        _includeCallerInfo = includeCallerInfo;
        _levelRegistry = levelRegistry;
        _minimumLevel = minimumLevel;
        _kernelHolder = kernelHolder;
        _dateTimeProvider = dateTimeProvider;
        // _namingPolicy stays nullable; null means "use Pascal default".
        // Storing null instead of PascalCasePolicy.Instance lets the multi-
        // policy interceptor (P3) tell at a glance whether the consumer
        // explicitly opted into a non-default policy.
        _namingPolicy = namingPolicy;
        _currentPolicyKind = ClassifyPolicyKind(namingPolicy);
        _allowExternalEventInjection = allowExternalEventInjection;
        // Coalesce here (not at the parameter) because the default-host channel
        // is not a compile-time constant. A null argument — every caller that
        // does not opt into a host channel — lands on Default, preserving the
        // pre-routing behaviour. ForContext passes the parent's resolved instance
        // straight through, so children never re-coalesce to Default.
        _runtimeMessages = runtimeMessages ?? MMP.Herald.Quick.HeraldHost.Default.RuntimeMessages;

        // Reject-path optimization: resolve the minimum level's rank
        // once AND snapshot the registry's rank table into a FrozenDictionary
        // so IsEnabled can do a direct Key-string lookup instead of a
        // virtual _levelRegistry.GetRank(level) dispatch. The snapshot is
        // safe because level-registry membership is stable for a given
        // pipeline's lifetime; a hot reload builds a new StructuredLogger
        // with its own snapshot.
        if (_levelRegistry is not null && _minimumLevel is not null)
        {
            _minimumLevelRank = _levelRegistry.GetRank(_minimumLevel);
            _rankByKey = BuildRankMap(_levelRegistry);

            // Precompute per-known-level accept booleans so typed handlers
            // (DebugLogInterpolatedStringHandler etc.) skip the full
            // IsEnabled call and do a single field-compare instead. Any
            // level not registered in this registry falls back to false
            // (reject) — matches the IsEnabled fallback semantics.
            _isVerboseAcceptable     = EvalAccept(_rankByKey, KnownLogLevels.Verbose, _minimumLevelRank);
            _isDebugAcceptable       = EvalAccept(_rankByKey, KnownLogLevels.Debug, _minimumLevelRank);
            _isInformationAcceptable = EvalAccept(_rankByKey, KnownLogLevels.Information, _minimumLevelRank);
            _isWarningAcceptable     = EvalAccept(_rankByKey, KnownLogLevels.Warning, _minimumLevelRank);
            _isErrorAcceptable       = EvalAccept(_rankByKey, KnownLogLevels.Error, _minimumLevelRank);
            _isFatalAcceptable       = EvalAccept(_rankByKey, KnownLogLevels.Fatal, _minimumLevelRank);
        }
        else
        {
            // No minimum configured → accept every known level. Matches
            // the IsEnabled(minimumLevelRank < 0) short-circuit.
            _isVerboseAcceptable     = true;
            _isDebugAcceptable       = true;
            _isInformationAcceptable = true;
            _isWarningAcceptable     = true;
            _isErrorAcceptable       = true;
            _isFatalAcceptable       = true;
        }
    }

    /// <summary>
    /// Recompute the per-known-level accept booleans against the supplied
    /// minimum rank and atomically publish them. Called by the hot-reload
    /// path's level-only branch so source-gen-emitted code reading
    /// <see cref="IsDebugAcceptable"/> etc. sees the new minimum without
    /// the pipeline being rebuilt.
    ///
    /// <para>
    /// Threading: each backing field is published with
    /// <see cref="System.Threading.Volatile.Write(ref bool, bool)"/> so a
    /// reader on a weakly-ordered architecture (ARM, Apple Silicon) cannot
    /// observe the new value before the producer's preceding writes. The
    /// six writes are independent — there is no atomic six-bool publish
    /// available, and the publish-order does not matter because the read
    /// side is one bool per call. A reader that races the publish sees
    /// either the pre- or post-reload value for its level, never a torn
    /// or interleaved value.
    /// </para>
    /// </summary>
    internal void RecomputeAcceptables(LogLevel? newMinimumLevel)
    {
        if (_rankByKey is null || newMinimumLevel is null)
        {
            // No registry / no minimum → accept-all.
            System.Threading.Volatile.Write(ref _isVerboseAcceptable, true);
            System.Threading.Volatile.Write(ref _isDebugAcceptable, true);
            System.Threading.Volatile.Write(ref _isInformationAcceptable, true);
            System.Threading.Volatile.Write(ref _isWarningAcceptable, true);
            System.Threading.Volatile.Write(ref _isErrorAcceptable, true);
            System.Threading.Volatile.Write(ref _isFatalAcceptable, true);
            return;
        }

        var rank = _rankByKey.TryGetValue(newMinimumLevel.Key, out var r) ? r : int.MaxValue;

        System.Threading.Volatile.Write(ref _isVerboseAcceptable,     EvalAccept(_rankByKey, KnownLogLevels.Verbose, rank));
        System.Threading.Volatile.Write(ref _isDebugAcceptable,       EvalAccept(_rankByKey, KnownLogLevels.Debug, rank));
        System.Threading.Volatile.Write(ref _isInformationAcceptable, EvalAccept(_rankByKey, KnownLogLevels.Information, rank));
        System.Threading.Volatile.Write(ref _isWarningAcceptable,     EvalAccept(_rankByKey, KnownLogLevels.Warning, rank));
        System.Threading.Volatile.Write(ref _isErrorAcceptable,       EvalAccept(_rankByKey, KnownLogLevels.Error, rank));
        System.Threading.Volatile.Write(ref _isFatalAcceptable,       EvalAccept(_rankByKey, KnownLogLevels.Fatal, rank));
    }

    private static bool EvalAccept(FrozenDictionary<string, int> ranks, LogLevel level, int minimumRank) =>
        ranks.TryGetValue(level.Key, out var rank) && rank >= minimumRank;

    private static FrozenDictionary<string, int> BuildRankMap(ILogLevelRegistry registry)
    {
        var registered = registry.GetRegisteredLevels();
        var map = new Dictionary<string, int>(registered.Count, StringComparer.Ordinal);
        foreach (var entry in registered)
        {
            map[entry.Level.Key] = entry.Rank;
        }
        // FrozenDictionary's ToFrozenDictionary honours the source comparer;
        // keep StringComparer.Ordinal so lookup uses the fastest string
        // compare/hash path.
        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    // -- ILogger --

    /// <summary>
    /// Forward a pre-built (hand-built) <see cref="LogEvent"/> directly into the
    /// pipeline. This is the <b>external event injection</b> port — the only
    /// boundary at which a caller hands Herald an event the pipeline did not
    /// build. It bypasses template parsing, enrichment, caller-info capture,
    /// time/scope/tenant stamping, and — critically — the redaction processors.
    ///
    /// <para>
    /// <b>Consent gate.</b> Injection is OFF by default. When the pipeline was
    /// built without <c>QuickLogBuilder.AllowExternalEventInjection()</c>, this
    /// call drops the event and fires a one-shot, un-silenceable runtime notice
    /// on <see cref="MMP.Herald.Diagnostics.HeraldRuntimeMessages"/> naming the
    /// call site, the protections the injected event skipped, and the opt-in.
    /// A log call never throws: the drop is silent to the caller's control flow
    /// and loud on the diagnostic channel. Enable injection with
    /// <c>AllowExternalEventInjection()</c>, which transfers responsibility for
    /// vetting the event (no unredacted secret/PII) to your application — see
    /// the license injection-liability note on that builder method.
    /// </para>
    ///
    /// <para>
    /// The typed methods (<see cref="Information(LogCategory, string, IReadOnlyDictionary{string, object?}?, IReadOnlyList{LogProperty}?, string?, string?, int)"/>,
    /// Debug, etc.) build through the factory and never reach this port, so
    /// they are unaffected by the consent gate. Use them for normal logging.
    /// </para>
    ///
    /// <para>
    /// The optional caller-info parameters are captured automatically by the
    /// compiler at direct call sites so the OFF-path notice can name where the
    /// injection came from. Calls made through the <see cref="ILogger"/>
    /// interface reference bind to the same method with these defaulted; the
    /// notice then names the type and the bypassed protections without a
    /// precise call site.
    /// </para>
    /// </summary>
    public void Log(
        LogEvent logEvent,
        [System.Runtime.CompilerServices.CallerMemberName] string? callerMember = null,
        [System.Runtime.CompilerServices.CallerFilePath] string? callerFile = null,
        [System.Runtime.CompilerServices.CallerLineNumber] int callerLine = 0)
    {
        // Entry-point discrimination (ADR section 7.1): this is the ONLY place the
        // consent flag is read. Internal events never arrive here — they forward
        // to _pipeline.Log(...) straight from the typed/core methods. So a false
        // flag here means "an external caller injected without consent."
        if (!_allowExternalEventInjection)
        {
            RefuseExternalInjection(callerMember, callerFile, callerLine);
            return;
        }

        _pipeline.Log(logEvent);
    }

    // Explicit ILogger.Log(LogEvent) implementation. The interface member takes
    // exactly one argument, so it cannot carry the caller-info parameters the
    // public overload above uses to name the OFF-path call site. Making it
    // explicit means a StructuredLogger-typed reference (logger.Log(ev)) binds
    // to the caller-info-bearing public overload and gets a precise call site,
    // while a call through an ILogger reference lands here and forwards with the
    // caller info defaulted — the notice then names the type, not the line. The
    // consent gate is identical on both paths because both flow through the
    // public overload.
    void ILogger.Log(LogEvent logEvent) =>
        // Pass explicit nulls so the caller-info parameters are NOT filled with
        // this forwarding frame (which would mis-name StructuredLogger.cs as the
        // call site). Null member + null file makes DescribeCallSite report the
        // honest "(call site unavailable — injected through an ILogger reference)".
        Log(logEvent, callerMember: null, callerFile: null, callerLine: 0);

#if NET9_0_OR_GREATER
    // -------------------------------------------------------------------------
    // Zero-alloc overloads (net9.0+): params ReadOnlySpan<LogProperty>
    //
    // These avoid heap-allocating a LogProperty[] at the call site.
    // The span is stack-allocated by the compiler when the caller passes
    // individual LogProperty values. Internally we copy to a list since
    // LogEvent stores IReadOnlyList<LogProperty>, but the caller pays
    // zero allocation for rejected calls (IsEnabled check is first).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Log with stack-allocated properties (net9.0+). Zero caller-side allocation.
    /// The ReadOnlySpan lives on the stack - no heap array is created at the call site.
    /// </summary>
    // Priority 17 = MaxArity (16) + 1 in TypedArgsOverloadGenerator. The
    // generator's typed-args overloads (1..16) carry priorities equal to
    // their arity; without this attribute, a call like
    //   _logger.Log(level, cat, template, new LogProperty(...))
    // would bind to Info<LogProperty>(LogCategory, string, LogProperty)
    // from the typed-args family rather than the params-span path here.
    [OverloadResolutionPriority(17)]
    public void Log(
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        params ReadOnlySpan<LogProperty> properties)
    {
        if (!IsEnabled(level)) return;

        // Span-aware kernel fast path. The kernel takes the span directly
        // via LogEventBuffer; the List<LogProperty> + CopyPropertiesToSpan
        // round-trip the chain path needs is pure allocation overhead in
        // the kernel case. Skip both. Mirrors the IReadOnlyList kernel
        // check at line 695, minus the eventId arg this overload doesn't
        // surface (it stays null on this path).
        if (TryAcquireKernel(out var kernel))
        {
            // IsEnabled above already applied the global dynamic floor; this
            // refines it with the per-category override map for the events the
            // direct-buffer path builds (it does not delegate to the core Log
            // entry, so the category-aware gate must run here).
            if (ShouldDropForDynamicLevel(category, level)) return;
            if (ShouldDropForSampler()) return;
            LogProperty[]? redactorRental = null;
            try
            {
                var transformedSpan = ApplyKernelTransforms(properties, out redactorRental);
                var buffer = new LogEventBuffer(
                    timeUtc: _dateTimeProvider!.GetUtcNow(),
                    level: level,
                    category: category,
                    messageTemplate: messageTemplate,
                    message: string.Empty,
                    properties: transformedSpan);
                kernel(in buffer);
            }
            finally
            {
                if (redactorRental is not null)
                    ArrayPool<LogProperty>.Shared.Return(redactorRental, clearArray: true);
            }
            return;
        }

        // Chain fallback: materialize a List once so the IReadOnlyList
        // overload below can drive the decorator chain. Rejected calls
        // already returned at the IsEnabled check above; this only fires
        // when the chain is actually going to consume the event.
        var propList = properties.Length > 0
            ? new List<LogProperty>(properties.Length)
            : null;

        if (propList is not null)
        {
            foreach (var prop in properties)
                propList.Add(prop);
        }

        Log(level, category, messageTemplate, propList);
    }

    /// <summary>
    /// Typed convenience: Information with span properties (net9.0+).
    /// </summary>
    [OverloadResolutionPriority(17)]
    public void Information(
        LogCategory category,
        string messageTemplate,
        params ReadOnlySpan<LogProperty> properties)
    {
        if (!IsInformationAcceptable) return;
        Log(KnownLogLevels.Information, category, messageTemplate, properties);
    }

    [OverloadResolutionPriority(17)]
    public void Debug(
        LogCategory category,
        string messageTemplate,
        params ReadOnlySpan<LogProperty> properties)
    {
        if (!IsDebugAcceptable) return;
        Log(KnownLogLevels.Debug, category, messageTemplate, properties);
    }

    [OverloadResolutionPriority(17)]
    public void Warning(
        LogCategory category,
        string messageTemplate,
        params ReadOnlySpan<LogProperty> properties)
    {
        if (!IsWarningAcceptable) return;
        Log(KnownLogLevels.Warning, category, messageTemplate, properties);
    }

    [OverloadResolutionPriority(17)]
    public void Error(
        LogCategory category,
        string messageTemplate,
        params ReadOnlySpan<LogProperty> properties)
    {
        if (!IsErrorAcceptable) return;
        Log(KnownLogLevels.Error, category, messageTemplate, properties);
    }

    [OverloadResolutionPriority(17)]
    public void Verbose(
        LogCategory category,
        string messageTemplate,
        params ReadOnlySpan<LogProperty> properties)
    {
        if (!IsVerboseAcceptable) return;
        Log(KnownLogLevels.Verbose, category, messageTemplate, properties);
    }
#endif

    // -------------------------------------------------------------------------
    // Compact-struct overloads (Project A): ReadOnlySpan<LogPropertyCompact>
    //
    // LogPropertyCompact is a 2-field readonly record struct. Callers pass a
    // ReadOnlySpan<LogPropertyCompact> instead of an IReadOnlyList<LogProperty>;
    // a LogPropertyCompact[] costs ~80 B for four entries (array header + four
    // inline structs) versus ~240 B for four LogProperty records. The kernel
    // fast path carries the compact span straight to the sinks without
    // materialising LogProperty records at all.
    //
    // These live outside the NET9_0_OR_GREATER block because they don't use
    // params ReadOnlySpan — plain ReadOnlySpan works from C# 8 / net6+.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Log with a span of compact-struct properties. The kernel fast path
    /// dispatches the span directly; the chain path inflates to a
    /// LogProperty[] at the boundary (the chain needs the legacy type).
    /// </summary>
    public void LogCompact(
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        ReadOnlySpan<LogPropertyCompact> properties)
    {
        if (!IsEnabled(level)) return;

        if (TryAcquireKernel(out var kernel))
        {
            // IsEnabled above already applied the global dynamic floor; this
            // refines it with the per-category override map for the direct-
            // buffer path (it does not delegate to the core Log entry).
            if (ShouldDropForDynamicLevel(category, level)) return;
            if (ShouldDropForSampler()) return;
            LogPropertyCompact[]? redactorRental = null;
            try
            {
                var transformedSpan = ApplyKernelTransforms(properties, out redactorRental);
                var buffer = new LogEventBuffer(
                    timeUtc: _dateTimeProvider!.GetUtcNow(),
                    level: level,
                    category: category,
                    messageTemplate: messageTemplate,
                    message: string.Empty,
                    compactProperties: transformedSpan);
                kernel(in buffer);
            }
            finally
            {
                if (redactorRental is not null)
                    ArrayPool<LogPropertyCompact>.Shared.Return(redactorRental, clearArray: true);
            }
            return;
        }

        if (properties.IsEmpty)
        {
            Log(level, category, messageTemplate, properties: (IReadOnlyList<LogProperty>?)null);
            return;
        }

        var inflated = new LogProperty[properties.Length];
        for (var i = 0; i < properties.Length; i++)
        {
            inflated[i] = properties[i].ToLogProperty();
        }
        Log(level, category, messageTemplate, inflated);
    }

    /// <summary>
    /// Compact-span overload that also carries a per-call context dictionary.
    /// Used by the Serilog-compat typed <b>exception</b> overloads: the
    /// exception rides in <paramref name="context"/> under
    /// <see cref="Services.LogContextKeys.Exception"/> because the kernel fast
    /// path has no exception slot.
    ///
    /// <para>
    /// <b>Why this is not the kernel path.</b> When <paramref name="context"/>
    /// is non-null the call always leaves the zero-alloc kernel route — the
    /// kernel's <see cref="LogEventBuffer"/> cannot carry a context dictionary.
    /// The compact span still avoids the <c>params object[]</c> array and the
    /// per-arg box that the legacy Serilog verb path pays: each value rode the
    /// typed <see cref="LogPropertyCompact"/> slot at the call site. Inflating
    /// to a <see cref="LogProperty"/> array here is one array allocation plus
    /// the unavoidable one box per value-type element (the same box the
    /// full-record path always pays); there is no second box and no params
    /// array. When <paramref name="context"/> is null this forwards to the
    /// plain <see cref="LogCompact(LogLevel, LogCategory, string, ReadOnlySpan{LogPropertyCompact})"/>
    /// overload and keeps the kernel fast path.
    /// </para>
    /// </summary>
    public void LogCompact(
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        ReadOnlySpan<LogPropertyCompact> properties,
        IReadOnlyDictionary<string, object?>? context)
    {
        // No context → identical to the kernel-eligible compact path.
        if (context is null)
        {
            LogCompact(level, category, messageTemplate, properties);
            return;
        }

        if (!IsEnabled(level)) return;

        // Context-bearing path: inflate the compact span to the full-record
        // array and route through Log(..., context), which the kernel skips
        // whenever context is non-null (see Log's kernel-eligibility guard).
        // No params object[] array, no per-arg box beyond the one box per
        // value-type element that LogProperty already requires.
        if (properties.IsEmpty)
        {
            Log(level, category, messageTemplate, properties: null, context: context);
            return;
        }

        var inflated = new LogProperty[properties.Length];
        for (var i = 0; i < properties.Length; i++)
        {
            inflated[i] = properties[i].ToLogProperty();
        }
        Log(level, category, messageTemplate, inflated, context);
    }

    /// <summary>Information-level compact-span overload.</summary>
    public void InformationCompact(
        LogCategory category,
        string messageTemplate,
        ReadOnlySpan<LogPropertyCompact> properties) =>
        LogCompact(KnownLogLevels.Information, category, messageTemplate, properties);

    /// <summary>Debug-level compact-span overload.</summary>
    public void DebugCompact(
        LogCategory category,
        string messageTemplate,
        ReadOnlySpan<LogPropertyCompact> properties) =>
        LogCompact(KnownLogLevels.Debug, category, messageTemplate, properties);

    /// <summary>Warning-level compact-span overload.</summary>
    public void WarningCompact(
        LogCategory category,
        string messageTemplate,
        ReadOnlySpan<LogPropertyCompact> properties) =>
        LogCompact(KnownLogLevels.Warning, category, messageTemplate, properties);

    /// <summary>Error-level compact-span overload.</summary>
    public void ErrorCompact(
        LogCategory category,
        string messageTemplate,
        ReadOnlySpan<LogPropertyCompact> properties) =>
        LogCompact(KnownLogLevels.Error, category, messageTemplate, properties);

    // -------------------------------------------------------------------------
    // Convenience methods — typed by level (all TFMs)
    // params was removed from properties to allow CallerMemberName/FilePath/LineNumber
    // as trailing optional parameters. Existing callers using named `properties: [...]`
    // syntax are unaffected. Direct positional params usage must use the Log() method.
    // -------------------------------------------------------------------------

    public void Verbose(
        LogCategory category,
        string messageTemplate,
        IReadOnlyDictionary<string, object?>? context = null,
        IReadOnlyList<LogProperty>? properties = null,
        [CallerMemberName] string? callerMember = null,
        [CallerFilePath] string? callerFile = null,
        [CallerLineNumber] int callerLine = 0) {
        if (!IsVerboseAcceptable) return;
        Log(KnownLogLevels.Verbose, category, messageTemplate, properties,
            EnrichContext(context, null, callerMember, callerFile, callerLine));
    }

    public void Verbose(
        LogCategory category,
        Exception exception,
        string messageTemplate,
        IReadOnlyDictionary<string, object?>? context = null,
        IReadOnlyList<LogProperty>? properties = null,
        [CallerMemberName] string? callerMember = null,
        [CallerFilePath] string? callerFile = null,
        [CallerLineNumber] int callerLine = 0) {
        if (!IsVerboseAcceptable) return;
        Log(KnownLogLevels.Verbose, category, messageTemplate, properties,
            EnrichContext(context, exception, callerMember, callerFile, callerLine));
    }

    public void Debug(
        LogCategory category,
        string messageTemplate,
        IReadOnlyDictionary<string, object?>? context = null,
        IReadOnlyList<LogProperty>? properties = null,
        [CallerMemberName] string? callerMember = null,
        [CallerFilePath] string? callerFile = null,
        [CallerLineNumber] int callerLine = 0) {
        if (!IsDebugAcceptable) return;
        Log(KnownLogLevels.Debug, category, messageTemplate, properties,
            EnrichContext(context, null, callerMember, callerFile, callerLine));
    }

    public void Debug(
        LogCategory category,
        Exception exception,
        string messageTemplate,
        IReadOnlyDictionary<string, object?>? context = null,
        IReadOnlyList<LogProperty>? properties = null,
        [CallerMemberName] string? callerMember = null,
        [CallerFilePath] string? callerFile = null,
        [CallerLineNumber] int callerLine = 0) {
        if (!IsDebugAcceptable) return;
        Log(KnownLogLevels.Debug, category, messageTemplate, properties,
            EnrichContext(context, exception, callerMember, callerFile, callerLine));
    }

    public void Information(
        LogCategory category,
        string messageTemplate,
        IReadOnlyDictionary<string, object?>? context = null,
        IReadOnlyList<LogProperty>? properties = null,
        [CallerMemberName] string? callerMember = null,
        [CallerFilePath] string? callerFile = null,
        [CallerLineNumber] int callerLine = 0) {
        if (!IsInformationAcceptable) return;
        Log(KnownLogLevels.Information, category, messageTemplate, properties,
            EnrichContext(context, null, callerMember, callerFile, callerLine));
    }

    public void Information(
        LogCategory category,
        Exception exception,
        string messageTemplate,
        IReadOnlyDictionary<string, object?>? context = null,
        IReadOnlyList<LogProperty>? properties = null,
        [CallerMemberName] string? callerMember = null,
        [CallerFilePath] string? callerFile = null,
        [CallerLineNumber] int callerLine = 0) {
        if (!IsInformationAcceptable) return;
        Log(KnownLogLevels.Information, category, messageTemplate, properties,
            EnrichContext(context, exception, callerMember, callerFile, callerLine));
    }

    public void Warning(
        LogCategory category,
        string messageTemplate,
        IReadOnlyDictionary<string, object?>? context = null,
        IReadOnlyList<LogProperty>? properties = null,
        [CallerMemberName] string? callerMember = null,
        [CallerFilePath] string? callerFile = null,
        [CallerLineNumber] int callerLine = 0) {
        if (!IsWarningAcceptable) return;
        Log(KnownLogLevels.Warning, category, messageTemplate, properties,
            EnrichContext(context, null, callerMember, callerFile, callerLine));
    }

    public void Warning(
        LogCategory category,
        Exception exception,
        string messageTemplate,
        IReadOnlyDictionary<string, object?>? context = null,
        IReadOnlyList<LogProperty>? properties = null,
        [CallerMemberName] string? callerMember = null,
        [CallerFilePath] string? callerFile = null,
        [CallerLineNumber] int callerLine = 0) {
        if (!IsWarningAcceptable) return;
        Log(KnownLogLevels.Warning, category, messageTemplate, properties,
            EnrichContext(context, exception, callerMember, callerFile, callerLine));
    }

    public void Fatal(
        LogCategory category,
        string messageTemplate,
        IReadOnlyDictionary<string, object?>? context = null,
        IReadOnlyList<LogProperty>? properties = null,
        [CallerMemberName] string? callerMember = null,
        [CallerFilePath] string? callerFile = null,
        [CallerLineNumber] int callerLine = 0) {
        if (!IsFatalAcceptable) return;
        Log(KnownLogLevels.Fatal, category, messageTemplate, properties,
            EnrichContext(context, null, callerMember, callerFile, callerLine));
    }

    public void Fatal(
        LogCategory category,
        Exception exception,
        string messageTemplate,
        IReadOnlyDictionary<string, object?>? context = null,
        IReadOnlyList<LogProperty>? properties = null,
        [CallerMemberName] string? callerMember = null,
        [CallerFilePath] string? callerFile = null,
        [CallerLineNumber] int callerLine = 0) {
        if (!IsFatalAcceptable) return;
        Log(KnownLogLevels.Fatal, category, messageTemplate, properties,
            EnrichContext(context, exception, callerMember, callerFile, callerLine));
    }

    public void Error(
        LogCategory category,
        string messageTemplate,
        IReadOnlyDictionary<string, object?>? context = null,
        IReadOnlyList<LogProperty>? properties = null,
        [CallerMemberName] string? callerMember = null,
        [CallerFilePath] string? callerFile = null,
        [CallerLineNumber] int callerLine = 0) {
        if (!IsErrorAcceptable) return;
        Log(KnownLogLevels.Error, category, messageTemplate, properties,
            EnrichContext(context, null, callerMember, callerFile, callerLine));
    }

    public void Error(
        LogCategory category,
        Exception exception,
        string messageTemplate,
        IReadOnlyDictionary<string, object?>? context = null,
        IReadOnlyList<LogProperty>? properties = null,
        [CallerMemberName] string? callerMember = null,
        [CallerFilePath] string? callerFile = null,
        [CallerLineNumber] int callerLine = 0) {
        if (!IsErrorAcceptable) return;
        Log(KnownLogLevels.Error, category, messageTemplate, properties,
            EnrichContext(context, exception, callerMember, callerFile, callerLine));
    }

    // -------------------------------------------------------------------------
    // Interpolated-string-handler overloads (zero-alloc rejection, lazy accept)
    //
    // These use the InterpolatedStringHandlerArgument pattern to hoist the
    // IsEnabled check ahead of every argument evaluation — the same trick
    // ZLogger uses. A call like _herald.InfoI(cat, $"User {name}") allocates
    // nothing at all when the level is below minimum: the compiler never
    // calls AppendFormatted, the property list stays null, the template
    // StringBuilder is never rented.
    //
    // On acceptance, the per-call allocations are the same the existing
    // params-ReadOnlySpan path already pays: N LogProperty records plus one
    // List<LogProperty>. The template StringBuilder is pooled.
    //
    // Property names come from [CallerArgumentExpression] on the handler's
    // AppendFormatted methods. $"User {name}" → property name "name". If a
    // caller wants a short, stable property name, assign to a local first.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Log with an interpolated string. Zero-allocation rejection when the
    /// level is below minimum; lazy template + property allocation on accept.
    /// </summary>
    public void Log(
        LogCategory category,
        LogLevel level,
        [InterpolatedStringHandlerArgument("", "level")]
        ref StructuredLogInterpolatedStringHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, properties) = handler.Drain();
        DispatchFromHandler(level, category, template, properties);
    }

    /// <summary>Verbose-level interpolated-string overload.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void VerboseI(
        LogCategory category,
        [InterpolatedStringHandlerArgument("")]
        ref TraceLogInterpolatedStringHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, properties) = handler.Drain();
        DispatchFromHandler(KnownLogLevels.Verbose, category, template, properties);
    }

    /// <summary>Debug-level interpolated-string overload.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DebugI(
        LogCategory category,
        [InterpolatedStringHandlerArgument("")]
        ref DebugLogInterpolatedStringHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, properties) = handler.Drain();
        DispatchFromHandler(KnownLogLevels.Debug, category, template, properties);
    }

    /// <summary>Information-level interpolated-string overload.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void InformationI(
        LogCategory category,
        [InterpolatedStringHandlerArgument("")]
        ref InfoLogInterpolatedStringHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, properties) = handler.Drain();
        DispatchFromHandler(KnownLogLevels.Information, category, template, properties);
    }

    /// <summary>Warning-level interpolated-string overload.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WarningI(
        LogCategory category,
        [InterpolatedStringHandlerArgument("")]
        ref WarnLogInterpolatedStringHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, properties) = handler.Drain();
        DispatchFromHandler(KnownLogLevels.Warning, category, template, properties);
    }

    /// <summary>Error-level interpolated-string overload.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ErrorI(
        LogCategory category,
        [InterpolatedStringHandlerArgument("")]
        ref ErrorLogInterpolatedStringHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, properties) = handler.Drain();
        DispatchFromHandler(KnownLogLevels.Error, category, template, properties);
    }

    // Shared handler-dispatch helper. The interpolated handler returns
    // properties as List<LogPropertyCompact>; we grab the underlying span via
    // CollectionsMarshal and hand it to the compact log path. Kernel-eligible
    // pipelines dispatch straight through; chain pipelines inflate once at
    // the LogCompact boundary.
    internal void DispatchFromHandler(
        LogLevel level,
        LogCategory category,
        string template,
        IReadOnlyList<LogPropertyCompact>? properties)
    {
        if (properties is null or { Count: 0 })
        {
            // Empty-list case still wants the rented object returned — the
            // handler may have rented on formattedCount > 0 even if every
            // AppendFormatted was skipped (shouldn't happen in practice,
            // but cheap to handle).
            if (properties is List<LogPropertyCompact> emptyList)
                LogPropertyCompactListPool.Return(emptyList);
            LogCompact(level, category, template, ReadOnlySpan<LogPropertyCompact>.Empty);
            return;
        }

        // Preferred path: grab a span over the List's backing array.
        // try/finally so a throwing sink still returns the list to the
        // pool — leaks here compound across accept calls until the thread
        // is recycled.
        if (properties is List<LogPropertyCompact> list)
        {
            try
            {
                LogCompact(level, category, template,
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list));
            }
            finally
            {
                LogPropertyCompactListPool.Return(list);
            }
            return;
        }

        // Fallback: arbitrary IReadOnlyList. Copy once. The handler always
        // returns a List today; this branch exists for future callers that
        // hand in a custom sequence.
        var array = new LogPropertyCompact[properties.Count];
        for (var i = 0; i < properties.Count; i++)
        {
            array[i] = properties[i];
        }
        LogCompact(level, category, template, array);
    }

    /// <summary>
    /// Bind this logger to a specific level (custom or built-in) and return a
    /// value-type handle that exposes the same interpolated-string-handler
    /// fast path as <see cref="InfoI"/> / <see cref="DebugI"/> / etc. Intended
    /// for user-registered custom levels — cache the returned
    /// <see cref="LevelBoundLogger"/> in a <c>static readonly</c> field and
    /// call <c>.LogI(cat, $"...")</c> from anywhere with zero-allocation
    /// rejection. Replaces the 20-line typed-handler wrapper otherwise needed
    /// per custom level.
    /// </summary>
    public LevelBoundLogger ForLevel(LogLevel level) => new(this, level);

    // -------------------------------------------------------------------------
    // Core Log method — used directly for custom levels, or called by typed methods
    // -------------------------------------------------------------------------

    public void Log(
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        IReadOnlyList<LogProperty>? properties = null,
        IReadOnlyDictionary<string, object?>? context = null,
        LogEventId? eventId = null)
    {
        // Category-aware dynamic floor gates EVERY path — kernel and chain
        // alike. The convenience methods (Information/Warning/...) pre-filter
        // on the static-only Is{Level}Acceptable shortcut, so the dynamic
        // floor must be enforced here at the authoritative entry or it leaks
        // on the chain fallback. Folds away when no resolver is installed.
        if (ShouldDropForDynamicLevel(category, level)) return;

        // Kernel fast path: only when the config is eligible AND this specific
        // call doesn't need any feature the kernel skips (per-call context,
        // event id, default context merge). The default-context field is
        // EmptyContext when the caller never called ForContext — the
        // reference-equality check is intentional and cheap.
        //
        // Volatile.Read so concurrent calls to SwapKernel (future hot-reload
        // integration) are safe — an in-flight call either sees the old
        // kernel or the new one, never a torn half-swap.
        if (context is null && eventId is null && TryAcquireKernel(out var kernel))
        {
            // Sampler drops happen here, before any property-shape branching
            // in DispatchViaKernel — the cheapest point to short-circuit.
            // The sampler runs once per accepted-by-level call. (Dynamic-floor
            // gating already happened above, ahead of the kernel/chain split.)
            if (ShouldDropForSampler()) return;
            DispatchViaKernel(kernel, level, category, messageTemplate, properties);
            return;
        }

        var logEvent = _eventFactory.Create(
            level,
            category,
            messageTemplate,
            properties,
            _defaultContext,
            context,
            eventId);

        _pipeline.Log(logEvent);
    }

    // Kernel dispatch: build a stack-scoped buffer and invoke the precompiled
    // kernel delegate. No heap LogEvent, no decorator chain. The properties
    // span is taken directly off the caller's array when possible, avoiding a
    // per-call copy. The kernel is passed in after the caller's volatile
    // snapshot so a concurrent SwapKernel cannot change the identity
    // mid-dispatch.
    private void DispatchViaKernel(
        LogKernel kernel,
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        IReadOnlyList<LogProperty>? properties)
    {
        // Three property-source shapes hit the kernel:
        //   1. null            → empty span
        //   2. LogProperty[]   → existing array, span over it (zero copy)
        //   3. IReadOnlyList<> → small-arity path uses a stack buffer;
        //                       only ≥ 9 entries fall to the heap-array
        //                       allocator.
        // The stack-buffer branch keeps the chain path allocation-free
        // for everything a structured log call can plausibly carry.
        if (properties is null)
        {
            DispatchKernelInner(kernel, level, category, messageTemplate, ReadOnlySpan<LogProperty>.Empty);
            return;
        }
        if (properties is LogProperty[] arr)
        {
            DispatchKernelInner(kernel, level, category, messageTemplate, arr.AsSpan());
            return;
        }
        var count = properties.Count;
        if (count <= 8)
        {
            // Stack buffer — InlineArray gives us 8 slots inline in the
            // local frame, no allocation, no GC pressure.
            var stackBuf = new MMP.Herald.Pipeline.Kernel.LogPropertyKernelBuffer8();
            for (var i = 0; i < count; i++) stackBuf[i] = properties[i];
            DispatchKernelInner(kernel, level, category, messageTemplate, ((Span<LogProperty>)stackBuf).Slice(0, count));
            return;
        }
        DispatchKernelInner(kernel, level, category, messageTemplate, CopyPropertiesToSpan(properties));
    }

    // Inner builder split out so the three property-shape branches above
    // do not each repeat the LogEventBuffer construction.
    private void DispatchKernelInner(
        LogKernel kernel,
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        ReadOnlySpan<LogProperty> propSpan)
    {
        // Fast-path redaction. When _fastRedactor is null this folds to a
        // single field-read + branch; when non-null, ApplyWithRental is
        // allocation-free on no-match and rents from ArrayPool on match.
        // The returned span aliases either the input (no rule fired) or
        // the rented array (a rule fired). The rental is returned to the
        // pool in finally so a throwing sink still releases it.
        LogProperty[]? redactorRental = null;
        try
        {
            var transformedSpan = ApplyKernelTransforms(propSpan, out redactorRental);

            var buffer = new LogEventBuffer(
                timeUtc: _dateTimeProvider!.GetUtcNow(),
                level: level,
                category: category,
                messageTemplate: messageTemplate,
                message: string.Empty,
                properties: transformedSpan);

            kernel(in buffer);
        }
        finally
        {
            if (redactorRental is not null)
                ArrayPool<LogProperty>.Shared.Return(redactorRental, clearArray: true);
        }
    }

    /// <summary>
    /// Apply the optional kernel-aware property transforms in order:
    /// redaction first (so enriched properties are not subject to the rule
    /// set), then enrichment. Both null-checks fold away under inlining
    /// when nothing is installed; the entire call collapses to a span
    /// pass-through on a vanilla pipeline.
    ///
    /// <para>
    /// Rental variant — the redactor rents its rewrite array from
    /// <see cref="ArrayPool{T}"/>. <paramref name="redactorRental"/> is non-null
    /// only when a redaction rule fired AND the redactor allocated. Caller
    /// MUST return the rental with <c>clearArray: true</c> after the kernel
    /// dispatch completes.
    /// </para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<LogProperty> ApplyKernelTransforms(
        ReadOnlySpan<LogProperty> input,
        out LogProperty[]? redactorRental)
    {
        var redactor = System.Threading.Volatile.Read(ref _fastRedactor);
        ReadOnlySpan<LogProperty> span;
        if (redactor is null)
        {
            redactorRental = null;
            span = input;
        }
        else
        {
            span = redactor.ApplyWithRental(input, out redactorRental, out _);
        }

        var enricher = System.Threading.Volatile.Read(ref _fastEnricher);
        return enricher is null ? span : enricher.Apply(span);
    }

    /// <summary>
    /// Compact-property overload of <see cref="ApplyKernelTransforms"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<LogPropertyCompact> ApplyKernelTransforms(
        ReadOnlySpan<LogPropertyCompact> input,
        out LogPropertyCompact[]? redactorRental)
    {
        var redactor = System.Threading.Volatile.Read(ref _fastRedactor);
        ReadOnlySpan<LogPropertyCompact> span;
        if (redactor is null)
        {
            redactorRental = null;
            span = input;
        }
        else
        {
            span = redactor.ApplyWithRental(input, out redactorRental, out _);
        }

        var enricher = System.Threading.Volatile.Read(ref _fastEnricher);
        return enricher is null ? span : enricher.Apply(span);
    }

    /// <summary>
    /// Atomically replace the active kernel. Pass <c>null</c> to force the
    /// decorator chain for all subsequent calls. Used by future hot-reload
    /// integration to invalidate a kernel built against an older sink
    /// configuration while the pipeline rebuilds, and to install a freshly
    /// compiled kernel once the new sinks are wired. Safe to call from any
    /// thread; in-flight <see cref="Log(LogLevel, LogCategory, string, IReadOnlyList{LogProperty}?, IReadOnlyDictionary{string, object?}?, LogEventId?)"/>
    /// calls continue on whichever kernel they captured via volatile read.
    /// </summary>
    internal void SwapKernel(LogKernel? newKernel) =>
        _kernelHolder.Swap(newKernel);

    /// <summary>
    /// Atomically install or clear the fast-path redactor. Pass <c>null</c> to
    /// disable redaction entirely. Called once at bootstrap when
    /// <c>QuickLogBuilder.WithFastRedaction(...)</c> is configured; future
    /// hot-reload integration can re-install with a fresh rule set.
    /// </summary>
    internal void InstallFastPathRedactor(FastPathRedactor? redactor) =>
        System.Threading.Interlocked.Exchange(ref _fastRedactor, redactor);

    /// <summary>
    /// Read the active fast-path redactor, or <c>null</c> when none is
    /// installed. Volatile-read so concurrent
    /// <see cref="InstallFastPathRedactor"/> calls are observed consistently.
    /// </summary>
    internal FastPathRedactor? FastPathRedactorOrNull =>
        System.Threading.Volatile.Read(ref _fastRedactor);

    /// <summary>
    /// Atomically install or clear the fast-path sampler. Pass <c>null</c>
    /// to disable sampling entirely. Same lifecycle as the fast redactor —
    /// installed once at bootstrap when
    /// <c>QuickLogBuilder.WithFastSampling(...)</c> is configured.
    /// </summary>
    internal void InstallFastPathSampler(FastPathSampler? sampler) =>
        System.Threading.Interlocked.Exchange(ref _fastSampler, sampler);

    /// <summary>Read the active fast-path sampler. Volatile-read.</summary>
    internal FastPathSampler? FastPathSamplerOrNull =>
        System.Threading.Volatile.Read(ref _fastSampler);

    /// <summary>
    /// Atomically install or clear the fast-path enricher. Same lifecycle
    /// as the other kernel-aware companions.
    /// </summary>
    internal void InstallFastPathEnricher(FastPathEnricher? enricher) =>
        System.Threading.Interlocked.Exchange(ref _fastEnricher, enricher);

    /// <summary>Read the active fast-path enricher. Volatile-read.</summary>
    internal FastPathEnricher? FastPathEnricherOrNull =>
        System.Threading.Volatile.Read(ref _fastEnricher);

    /// <summary>
    /// Atomically install or clear the fast-path dynamic-level resolver.
    /// Same lifecycle as the other kernel-aware companions.
    /// </summary>
    internal void InstallFastPathDynamicLevel(FastPathDynamicLevel? resolver) =>
        System.Threading.Interlocked.Exchange(ref _fastDynamicLevel, resolver);

    /// <summary>Read the active fast-path dynamic level. Volatile-read.</summary>
    internal FastPathDynamicLevel? FastPathDynamicLevelOrNull =>
        System.Threading.Volatile.Read(ref _fastDynamicLevel);

    // Optional kernel-aware async sink wrapper. Installed by
    // QuickLogBuilder.WithFastAsyncSink — it wraps the routed-sinks
    // composite, pulls events off-thread, and forwards them to the inner
    // composite without blocking the caller. Tracked here so hot-reload
    // can find the active instance and drain it before installing the
    // replacement.
    private FastPathAsyncSink? _fastAsyncSink;

    /// <summary>
    /// Atomically install or clear the fast-path async sink wrapper.
    /// Returns the previously-installed instance (or null) so the caller
    /// can drain + dispose it as part of a swap. Hot-reload uses the
    /// returned reference to retire the prior wrapper after the new one
    /// is wired into the kernel.
    /// </summary>
    internal FastPathAsyncSink? InstallFastPathAsyncSink(FastPathAsyncSink? sink) =>
        System.Threading.Interlocked.Exchange(ref _fastAsyncSink, sink);

    /// <summary>Read the active fast-path async sink wrapper. Volatile-read.</summary>
    internal FastPathAsyncSink? FastPathAsyncSinkOrNull =>
        System.Threading.Volatile.Read(ref _fastAsyncSink);

    /// <summary>
    /// Drop check for the dynamic level. Folds away when no resolver is
    /// installed; on a configured resolver returns true for "drop this
    /// event, do not build a buffer." Inlined so the conditional
    /// collapses into the caller frame. Runs before the sampler so a
    /// level-rejection saves the sampler's Interlocked.Increment too.
    ///
    /// <para>
    /// Threads the category through to the resolver so a per-category
    /// override map can take precedence over the global switch. The
    /// resolver branches to the global-only path when no map is
    /// configured (the dominant case), so this overload costs the same
    /// as the legacy global-only one in that configuration.
    /// </para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ShouldDropForDynamicLevel(LogCategory category, LogLevel level)
    {
        var resolver = System.Threading.Volatile.Read(ref _fastDynamicLevel);
        return resolver is not null && !resolver.ShouldAccept(category, level);
    }

    /// <summary>
    /// Drop check at the dispatch boundary. Folds away when no sampler is
    /// installed; on a configured sampler returns true for "drop this
    /// event, do not build a buffer." Inlined so the conditional collapses
    /// into the caller frame.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ShouldDropForSampler()
    {
        var sampler = System.Threading.Volatile.Read(ref _fastSampler);
        return sampler is not null && !sampler.ShouldEmit();
    }

    /// <summary>
    /// Read the active kernel delegate, or <c>null</c> when the pipeline
    /// configuration isn't kernel-eligible. Volatile-read so concurrent
    /// <see cref="SwapKernel"/> calls are observed consistently. Exposed
    /// to sibling low-level loggers (HotPathLogger) so they can share the
    /// same kernel without re-running eligibility.
    /// </summary>
    internal LogKernel? KernelOrNull => _kernelHolder.Current;

    /// <summary>
    /// Read the active DateTimeProvider used by the kernel path. Exposed
    /// alongside <see cref="KernelOrNull"/> so HotPathLogger-style clients
    /// can build a <see cref="LogEventBuffer"/> with the same clock the
    /// kernel path uses. Null when no provider was injected at construction.
    /// </summary>
    internal IDateTimeProvider? KernelDateTimeProvider => _dateTimeProvider;

    /// <summary>
    /// Kernel-fast-path eligibility — three conditions every overload
    /// that targets the kernel needs to satisfy:
    ///
    /// <list type="number">
    ///   <item>An active kernel is bound (volatile read; SwapKernel
    ///         coordinates publishing).</item>
    ///   <item>A clock is wired so the kernel path can stamp the
    ///         buffer's timestamp.</item>
    ///   <item>The default context is the canonical empty one — a
    ///         mutated default context implies the chain has stable
    ///         enrichment to apply.</item>
    /// </list>
    ///
    /// <para>The IReadOnlyList overload adds <c>context is null</c> and
    /// <c>eventId is null</c> on top because those args are only on
    /// that surface; the compact / buffer overloads can call this
    /// helper directly. AggressiveInlining so the JIT folds it back
    /// to identical IL at every call site.</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryAcquireKernel(out LogKernel kernel)
    {
        var k = _kernelHolder.Current;
        if (k is not null
            && _dateTimeProvider is not null
            && ReferenceEquals(_defaultContext, LogEvent.EmptyContext))
        {
            kernel = k;
            return true;
        }
        kernel = null!;
        return false;
    }

    // Heap fallback for 9-or-more-property IReadOnlyList sources. Below
    // 9 entries we use a stack InlineArray (see DispatchViaKernel above).
    private static ReadOnlySpan<LogProperty> CopyPropertiesToSpan(IReadOnlyList<LogProperty> source)
    {
        var array = new LogProperty[source.Count];
        for (var i = 0; i < array.Length; i++)
        {
            array[i] = source[i];
        }
        return array.AsSpan();
    }

    /// <summary>
    /// Returns true when the given level is at or above the configured minimum level.
    /// Always returns true when no minimum level was provided at construction.
    /// </summary>
    /// <remarks>
    /// P1 reject-path optimization: when a minimum level is configured, this
    /// method compares the candidate's rank against the cached minimum rank —
    /// one dictionary lookup on the candidate instead of two (candidate +
    /// minimum). Marked <c>AggressiveInlining</c> so the JIT collapses the
    /// call into the caller frame, turning the hot reject path into a
    /// single rank-lookup + compare.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsEnabled(LogLevel level)
    {
        // An event must clear BOTH floors. The static floor is configured at
        // build time (WithMinimumLevel); the dynamic floor is a runtime-mutable
        // switch (WithFastDynamicLevel / Serilog's ControlledBy). They compose:
        // a candidate is enabled only when it passes the static rank check AND
        // the dynamic switch's current level. The dynamic check runs first only
        // when a resolver is installed — the field read folds to a null check
        // for the dominant no-dynamic-switch case, so the static accept-all
        // path below stays a single compare + branch.
        var resolver = System.Threading.Volatile.Read(ref _fastDynamicLevel);
        if (resolver is not null)
        {
            // Honour the same null contract on both branches: the resolver's
            // rank lookup would NRE on a null Key, so reject null here exactly
            // as the static path's ThrowLevelNull does (consistency over a
            // path-dependent exception type).
            if (level is null) ThrowLevelNull();
            if (!resolver.ShouldAccept(level)) return false;
        }

        return IsEnabledByStaticFloor(level);
    }

    // Static-floor-only acceptance. Split out of IsEnabled so the dynamic floor
    // composes in front of it without duplicating the rank logic. Every dispatch
    // path that gates on the static floor reaches the same decision here; the
    // dynamic floor is layered on by IsEnabled (global) and the per-category
    // ShouldDropForDynamicLevel check at the Log() entry (category-aware).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsEnabledByStaticFloor(LogLevel level)
    {
        // Fast path: no minimum configured → accept everything. Hit
        // first so the null check and dictionary lookup are skipped
        // entirely when filtering is off. One cmp + branch.
        if (_minimumLevelRank < 0)
        {
            return true;
        }

        // Known-level fast path. ReferenceEquals on the KnownLogLevels
        // singletons collapses to a pointer compare, so callers passing
        // KnownLogLevels.Information / Debug / etc. skip the FrozenDictionary
        // lookup entirely — one field read + branch. Custom levels fall
        // through to the lookup path below.
        if (ReferenceEquals(level, KnownLogLevels.Information)) return IsInformationAcceptable;
        if (ReferenceEquals(level, KnownLogLevels.Debug)) return IsDebugAcceptable;
        if (ReferenceEquals(level, KnownLogLevels.Warning)) return IsWarningAcceptable;
        if (ReferenceEquals(level, KnownLogLevels.Error)) return IsErrorAcceptable;
        if (ReferenceEquals(level, KnownLogLevels.Verbose)) return IsVerboseAcceptable;
        if (ReferenceEquals(level, KnownLogLevels.Fatal)) return IsFatalAcceptable;

        // Arg-null moved here (off the hit-every-call accept-all path)
        // and outlined to keep the JIT-inlined hot body small.
        if (level is null) ThrowLevelNull();

        // Direct FrozenDictionary lookup keyed on Key. Replaces the
        // virtual _levelRegistry.GetRank(level) call — no interface
        // dispatch, no inner arg-null, no extra method frame. Unknown
        // levels fall through to reject (safer than throwing and
        // matches what a dynamic level registry should do for a
        // not-yet-registered level key).
        return _rankByKey is not null
            && _rankByKey.TryGetValue(level!.Key, out var rank)
            && rank >= _minimumLevelRank;
    }

    // [DoesNotReturn] lets nullable flow-analysis narrow `level` to non-null
    // after every call site (IsEnabled's dynamic branch + the static path),
    // so the resolver/lookup that follows needs no null-forgiving operator.
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowLevelNull() =>
        throw new ArgumentNullException("level");

    public ILogScope BeginScope(IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return _scopeProvider.BeginScope(values);
    }

    public StructuredLogger ForContext(IReadOnlyDictionary<string, object?> defaultContext)
    {
        ArgumentNullException.ThrowIfNull(defaultContext);

        // Share the parent's KernelHolder reference (not the kernel value)
        // so a SwapKernel on the parent reaches the child too. Capturing
        // _kernelHolder.Current at this point would re-introduce the orphan
        // bug — a hot reload after ForContext returned would update the
        // parent's holder but leave the child dispatching through the old
        // delegate.
        return new StructuredLogger(
            _pipeline,
            _eventFactory,
            _scopeProvider,
            MergeContexts(_defaultContext, defaultContext),
            _includeCallerInfo,
            _levelRegistry,
            _minimumLevel,
            _kernelHolder,
            _dateTimeProvider,
            _namingPolicy,
            _allowExternalEventInjection,
            // Propagate the parent's already-resolved runtime-message channel by
            // reference — same discipline as _kernelHolder above. A child that
            // re-coalesced to Default would publish its refusal/announcement to the
            // wrong host buffer and re-open the cross-host bleed. Non-negotiable;
            // pinned by RuntimeNoticePerHostIsolationStressTests.
            _runtimeMessages);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    // Cognitive Complexity note: this single method handles all context enrichment so
    // typed methods stay one-liners. It short-circuits immediately when nothing needs
    // adding, avoiding any allocation on the common path.
    private IReadOnlyDictionary<string, object?>? EnrichContext(
        IReadOnlyDictionary<string, object?>? context,
        Exception? exception,
        string? callerMember,
        string? callerFile,
        int callerLine)
    {
        var needsException = exception is not null;
        var needsCallerInfo = _includeCallerInfo && callerMember is not null;

        if (!needsException && !needsCallerInfo)
        {
            return context;
        }

        var merged = context is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(context, StringComparer.Ordinal);

        if (needsException)
        {
            merged[Services.LogContextKeys.Exception] = exception;
        }

        if (needsCallerInfo)
        {
            merged[Services.LogContextKeys.CallerMember] = callerMember;
            merged[Services.LogContextKeys.CallerFile] = GetCachedFileName(callerFile);
            merged[Services.LogContextKeys.CallerLine] = callerLine;
        }

        return merged;
    }

    /// <summary>
    /// Cache the last Path.GetFileName result. Consecutive calls from the same file
    /// (common pattern) skip the path parsing entirely. Not thread-safe across concurrent
    /// loggers, but StructuredLogger is typically used from one context at a time.
    /// Worst case on race: Path.GetFileName runs twice — correct, just not cached.
    /// </summary>
    private string? GetCachedFileName(string? callerFile)
    {
        if (callerFile is null) return null;
        if (string.Equals(callerFile, _lastCallerFileFull, StringComparison.Ordinal))
            return _lastCallerFileName;

        var fileName = Path.GetFileName(callerFile);
        _lastCallerFileFull = callerFile;
        _lastCallerFileName = fileName;
        return fileName;
    }

    private static IReadOnlyDictionary<string, object?> MergeContexts(
        IReadOnlyDictionary<string, object?> left,
        IReadOnlyDictionary<string, object?> right)
    {
        var merged = new Dictionary<string, object?>(left, StringComparer.Ordinal);

        foreach (var pair in right)
        {
            merged[pair.Key] = pair.Value;
        }

        return merged;
    }

    // Upgrade note for C# 14: the EnrichContext allocation-avoidance guard may become
    // expressible more concisely with upcoming collection / null-coalescing improvements.
    // Current implementation is kept for C# 12 / .NET 8 compatibility.

    // -- IComponentMetadata --
    string IComponentMetadata.ComponentName => "preset:structured";
    string IComponentMetadata.DisplayName => "Structured Preset";
    string IComponentMetadata.Description => "Event creation preset: full template parsing, enrichment, caller info, scoped context.";
    string IComponentMetadata.Help => "The default event creation preset. Parses message templates, runs enrichers, captures caller info, supports scoped context. Events then flow through the shared decorator chain. ~576-1,091ns per accepted event (BDN). Use for system events, lifecycle tracking, business transitions. For game loop hot paths, use the HotPath preset instead.";
    VendorInfo IComponentMetadata.Vendor => VendorInfo.MMP;
    Configuration.PipelineStepRules IComponentMetadata.Rules => Configuration.PipelineStepRules.Default;
    internal static readonly System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> DefaultSchema =
    [
        Routing.SinkConfigField.Bool("includeCallerInfo", false, "Capture caller member/file/line",
            "When enabled, each log event includes the calling method name, source file path, and line number via [CallerMemberName], [CallerFilePath], [CallerLineNumber]. Adds ~200ns per event. Useful for debugging but may expose source paths in production logs."),
    ];
    System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> IComponentMetadata.ConfigurationSchema =>
    [
        DefaultSchema[0] with { DefaultValue = _includeCallerInfo },
    ];
}
