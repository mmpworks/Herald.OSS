#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MMP.Herald.Configuration;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Events;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Templating;
using MMP.Herald.Time;

namespace MMP.Herald.Bootstrap;

/// <summary>
/// Orchestrates hot reload of the logging pipeline when configuration changes.
///
/// Fast path: if only the minimum level changed, updates the LogLevelSwitch without
/// rebuilding the pipeline. Slow path: rebuilds the full inner pipeline and swaps it
/// into the SwappableLogger, then disposes the old pipeline resources.
///
/// Uses SemaphoreSlim(1,1) to serialize reload operations.
/// </summary>
public sealed class HotReloadableLoggingBootstrap : IDisposable
{
    private readonly SwappableLogger _swappableLogger;
    // The outer-most StructuredLogger that wraps _swappableLogger. Held so a
    // reload can swap its cached kernel reference alongside the inner-pipeline
    // swap. Without this, the kernel fast path in StructuredLogger keeps
    // dispatching to the old (orphaned) pipeline's kernel after a rebuild —
    // SwappableLogger only catches the slow path. Optional: a host that
    // bypasses the kernel fast path can construct without supplying this and
    // keep the prior behavior.
    private readonly StructuredLogger? _structuredLogger;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly LoggingHostAdapters? _hostAdapters;
    private readonly IReadOnlyList<IDestructuringPolicy>? _destructuringPolicies;
    private readonly IReadOnlyList<Routing.ILogSinkProvider>? _additionalSinkProviders;
    private readonly IReadOnlyDictionary<string, object?>? _defaultContext;
    private readonly ILogFailureSink? _failureSink;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    // Captured per-instance so two concurrent bootstraps cannot race on a
    // shared timeout knob. Defaults to 30 seconds (the production value);
    // tests inject a smaller window through the constructor parameter.
    private readonly TimeSpan _oldResourceDisposeTimeout;

    // The janitor owns the bounded-dispose-with-telemetry shape. The
    // bootstrap forwards old-resource teardown and any hot-reload fault
    // it wants to publish (drain cap, dispose-with-pending) through this
    // single seam so all telemetry exits one place.
    private readonly OldResourceJanitor _janitor;

    // Most-recent-wins queue for reload requests that arrive while a reload
    // is already in flight. The in-flight reload drains this slot after its
    // ExecuteReload returns, so two file-watcher events inside one slow
    // rebuild apply both edits — the latest content overwrites any earlier
    // pending content because operators care about the live state, not the
    // history.
    //
    // Cognitive Complexity: the field interacts with two locks. _reloadLock
    // gates execution; _pendingLock gates writes to _pendingJson. Splitting
    // them keeps a Wait(0) caller from blocking on the slow run that owns
    // _reloadLock — the Deferred caller writes one slot and returns.
    private readonly object _pendingLock = new();
    private string? _pendingJson;

    private IAsyncDisposable? _currentAsyncResource;
    private LogLevelSwitch? _currentGlobalSwitch;
    private LoggingRuntimeConfiguration? _currentConfig;
    // Track the live registry so the management API surface (GET
    // /api/registry/{name}/levels) reflects post-reload state. Without
    // this, QuickLogResult.LevelRegistry would keep returning the
    // original registry built at construction even after a reorder
    // committed via Reload(json) replaced the runtime registry.
    private ILogLevelRegistry? _currentLevelRegistry;

    // Live PipelineAccessor for the current chain. Captured by every
    // slow rebuild so the next reload's IsSinkPropertyOnly delta path
    // can find the live SafeCompositeLogger to swap children into. Null
    // until the first slow rebuild populates it.
    private Pipeline.PipelineAccessor? _currentPipelineAccessor;
    // Active reload source. Owns its own debounce / change detection;
    // the bootstrap consumes the callback and forwards into Reload(json).
    // Pre-fix this slot was typed ConfigurationFileWatcher? — extracting
    // IConfigReloadSource lets a host plug Consul / K8s / push sources
    // without growing parallel WatchXxx(...) overloads each duplicating
    // debounce + error-routing glue (principal-review queue #12).
    private IConfigReloadSource? _reloadSource;

    // Source identifier passed into Reload via the most recent reload
    // source callback. Null for direct Reload(json) calls. Carried through
    // to ReloadDiagnostics so subscribers know which file / KV key drove
    // the change.
    private string? _currentReloadPath;

    private bool _isDisposed;

    /// <summary>
    /// Fires after a successful reload run with a
    /// <see cref="ReloadDiagnostics"/> payload carrying the outcome,
    /// source path, wall-clock duration, and null exception. Sister
    /// surface to the tenant-observation events on
    /// <see cref="Quick.HeraldRegistryInstance"/>: subscribe once at
    /// startup to drive dashboards, audit trails, or chaos-test
    /// harnesses without polling
    /// <see cref="CurrentMinimumLevel"/>.
    ///
    /// <para>
    /// A throwing subscriber propagates out of the event but does not
    /// roll back the reload — the pipeline has already swapped. Wrap
    /// handler bodies in try/catch if breakage in a third-party observer
    /// must not surface to the source of the change.
    /// </para>
    /// </summary>
    public event Action<ReloadDiagnostics>? OnReloadCompleted;

    /// <summary>
    /// Fires when a reload attempt failed. Payload carries the original
    /// outcome (<see cref="HotReloadOutcome.Applied"/> indicates the
    /// foreground attempt threw mid-rebuild), source path, wall-clock
    /// duration up to the failure, and the terminating exception.
    /// </summary>
    public event Action<ReloadDiagnostics>? OnReloadFailed;

    public HotReloadableLoggingBootstrap(
        SwappableLogger swappableLogger,
        IDateTimeProvider dateTimeProvider,
        IAsyncDisposable? currentAsyncResource,
        LogLevelSwitch? currentGlobalSwitch = null,
        LoggingHostAdapters? hostAdapters = null,
        IReadOnlyList<IDestructuringPolicy>? destructuringPolicies = null,
        IReadOnlyDictionary<string, object?>? defaultContext = null,
        ILogFailureSink? failureSink = null,
        IReadOnlyList<Routing.ILogSinkProvider>? additionalSinkProviders = null,
        StructuredLogger? structuredLogger = null,
        TimeSpan? oldResourceDisposeTimeout = null)
    {
        _swappableLogger = swappableLogger ?? throw new ArgumentNullException(nameof(swappableLogger));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        _currentAsyncResource = currentAsyncResource;
        _currentGlobalSwitch = currentGlobalSwitch;
        _hostAdapters = hostAdapters;
        _destructuringPolicies = destructuringPolicies;
        _additionalSinkProviders = additionalSinkProviders;
        _defaultContext = defaultContext;
        _failureSink = failureSink;
        _structuredLogger = structuredLogger;
        _oldResourceDisposeTimeout = oldResourceDisposeTimeout ?? DefaultOldResourceDisposeTimeout;
        _janitor = new OldResourceJanitor(failureSink, _oldResourceDisposeTimeout);
    }

    /// <summary>
    /// The level registry currently in effect. Returns the registry
    /// the most recent <see cref="Reload(string)"/> built — or null if
    /// no reload has happened yet, in which case callers fall back to
    /// the registry the original bootstrap supplied. Surfaces here so
    /// the management API can reflect post-reorder state through
    /// <c>GET /api/registry/{name}/levels</c> without a process restart.
    /// </summary>
    public ILogLevelRegistry? CurrentLevelRegistry => _currentLevelRegistry;

    /// <summary>
    /// The pipeline minimum level currently in effect. Reload() rewrites
    /// this whenever the new JSON shifts the floor (most commonly via
    /// <c>SetMinimumLevel</c> from the management API). Returns null until
    /// the first reload, at which point callers fall back to the snapshot
    /// the original bootstrap supplied. Without this hook, every
    /// HotPath logger created post-reload would inherit the build-time
    /// minimum and reject events the live pipeline accepts.
    /// </summary>
    public LogLevel? CurrentMinimumLevel => _currentConfig?.PipelinePolicy.MinimumLevel;

    /// <summary>
    /// The dynamic-level policy currently in effect. Reload() swaps out
    /// the global level switch on every rebuild, so the original snapshot
    /// hands callers a switch that no longer drives the pipeline. The
    /// management-API surface delegates here so a <c>SetMinimumLevel</c>
    /// run with dynamic levels enabled reaches the right switch.
    /// </summary>
    public DynamicLevelPolicy? CurrentDynamicLevelPolicy => _currentConfig?.PipelinePolicy.DynamicLevelPolicy;

    /// <summary>
    /// Starts watching the specified config file for changes. Thin
    /// adapter that constructs a <see cref="FileConfigReloadSource"/> and
    /// hands it to <see cref="UseReloadSource"/>; hosts that need a
    /// non-file source (Consul KV, K8s ConfigMap, management-API push)
    /// call <see cref="UseReloadSource"/> directly with their own
    /// <see cref="IConfigReloadSource"/> implementation.
    /// </summary>
    public void WatchFile(string filePath, int debounceMs = 500)
    {
        UseReloadSource(new FileConfigReloadSource(filePath, debounceMs));
    }

    /// <summary>
    /// Attach an arbitrary <see cref="IConfigReloadSource"/>. Replaces the
    /// previous source if any (the previous one is disposed). The
    /// supplied source's <see cref="IConfigReloadSource.Start"/> runs
    /// inline; failures there propagate out so a misconfigured source is
    /// loud at attachment time, not silent at first change.
    /// </summary>
    public void UseReloadSource(IConfigReloadSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _reloadSource?.Dispose();
        _reloadSource = source;
        source.Start(OnReloadSourceChanged);
    }

    // Adapter for the IConfigReloadSource callback. Records the source
    // path so Reload's diagnostics carry it, then forwards into the
    // standard Reload(json) path. Any exception during the file read
    // surfaces through the failure sink so a watcher fire mid-save does
    // not crash the watcher thread.
    private void OnReloadSourceChanged(string sourcePath, string json)
    {
        if (_isDisposed) return;

        try
        {
            _currentReloadPath = sourcePath;
            Reload(json);
        }
        catch (Exception ex)
        {
            _failureSink?.ReportFailure(
                new LogEvent(
                    DateTimeOffset.UtcNow,
                    KnownLogLevels.Error,
                    LogCategory.App,
                    "Hot reload failed for source: {SourcePath}",
                    $"Hot reload failed for source: {sourcePath}",
                    LogEvent.EmptyProperties,
                    LogEvent.EmptyContext),
                ex,
                "HotReload");
        }
    }

    /// <summary>
    /// Switch to a different configuration file at runtime.
    /// Stops watching the old file, loads the new config, rebuilds the pipeline,
    /// and starts watching the new file for changes.
    ///
    /// Use cases: switch from logging-normal.json to logging-debug.json
    /// when investigating an issue, or logging-tournament.json during competitive play.
    /// </summary>
    public void SwitchConfigFile(string newFilePath, int debounceMs = 500)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newFilePath);

        if (!File.Exists(newFilePath))
        {
            throw new FileNotFoundException(
                $"Configuration file not found: {newFilePath}", newFilePath);
        }

        var json = File.ReadAllText(newFilePath);
        Reload(json);
        WatchFile(newFilePath, debounceMs);
    }

    /// <summary>
    /// Manually triggers a reload from the given JSON configuration string.
    /// Returns <see cref="HotReloadOutcome.Applied"/> when the JSON ran,
    /// <see cref="HotReloadOutcome.Deferred"/> when a reload was already in
    /// flight (the JSON is queued and the in-flight reload will pick it up),
    /// or <see cref="HotReloadOutcome.Skipped"/> when the bootstrap is
    /// disposed. The drain loop after a successful run picks up any
    /// most-recent JSON that landed while the slow rebuild was running, so
    /// two edits inside one rebuild window both apply.
    /// </summary>
    public HotReloadOutcome Reload(string jsonConfigString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonConfigString);

        if (_isDisposed) return HotReloadOutcome.Skipped;

        if (!_reloadLock.Wait(0))
        {
            QueuePendingReload(jsonConfigString);
            return HotReloadOutcome.Deferred;
        }

        try
        {
            DrainAndExecute(jsonConfigString);
        }
        finally
        {
            _reloadLock.Release();
        }

        // After releasing, check if a Wait(0) caller raced in between our
        // last in-lock drain and the release. If we can re-acquire, drain
        // again; if we can't, another Reload is now in flight and will
        // drain whatever we leave behind. Bounded so a pathological
        // hammering watcher cannot starve the caller.
        TryReclaimAndDrain();

        return HotReloadOutcome.Applied;
    }

    // Bound the drain so a pathological "every commit also writes one
    // more pending edit" stream cannot starve the lock release. On
    // overflow, the surplus is reported through the failure sink so
    // operators see the throttle without losing visibility. Internal so
    // tests can shorten the cap and exercise the overflow path
    // deterministically — production callers leave it at the default.
    internal int MaxDrains = 16;

    // Owns the lock. Runs the supplied JSON, then loops while subsequent
    // Wait(0) callers have left newer JSON in the pending slot. ExecuteReload
    // is allowed to throw — the loop short-circuits on exception and the
    // pending slot stays populated for the next caller to drain.
    private void DrainAndExecute(string firstJson)
    {
        var current = firstJson;
        var cap = MaxDrains;
        for (var i = 0; i < cap; i++)
        {
            ExecuteReload(current);
            var next = TakePendingJson();
            if (next is null) return;
            current = next;
        }

        // Cap reached with `current` holding a JSON we never ran. Re-queue
        // it for the next drainer to pick up and tell the operator we
        // throttled. Without this re-queue the deferred JSON would be
        // silently lost — exactly the seam the cap was meant to bound,
        // not the seam we wanted to introduce.
        QueuePendingReload(current);
        ReportDisposalFailure(
            new InvalidOperationException(
                $"Hot reload drain exceeded {cap} iterations; the latest pending JSON has been re-queued for the next reload."),
            "HotReloadDrainCapped");
    }

    // After Release, re-check the pending slot. If non-null AND no other
    // thread is currently holding the lock, take ownership and drain. If
    // another Reload arrived in the gap, they hold the lock and will
    // drain the slot themselves on their way out.
    private void TryReclaimAndDrain()
    {
        const int MaxReentries = 4;
        for (var i = 0; i < MaxReentries; i++)
        {
            if (PeekPendingJson() is null) return;
            if (!_reloadLock.Wait(0)) return;

            try
            {
                var pending = TakePendingJson();
                if (pending is null) return;
                DrainAndExecute(pending);
            }
            finally
            {
                _reloadLock.Release();
            }
        }
    }

    private string? PeekPendingJson()
    {
        lock (_pendingLock) return _pendingJson;
    }

    private void QueuePendingReload(string jsonConfigString)
    {
        lock (_pendingLock)
        {
            _pendingJson = jsonConfigString;
        }
    }

    private string? TakePendingJson()
    {
        lock (_pendingLock)
        {
            var taken = _pendingJson;
            _pendingJson = null;
            return taken;
        }
    }

    // ExecuteReload is the orchestrator. It deserialises the JSON, runs
    // the diff (if a prior config exists), and dispatches to one of three
    // outcomes: level-only fast path, sinks-only delta path, or the slow
    // full reconstruct + swap. Wraps each path in start/stop timing so
    // OnReloadCompleted / OnReloadFailed subscribers see consistent
    // duration numbers (principal-review queue #14 + #8).
    private void ExecuteReload(string jsonConfigString)
    {
        var sw = Stopwatch.StartNew();
        var sourcePath = _currentReloadPath;
        try
        {
            var jsonConfig = LoggingJsonSerializer.Deserialize(jsonConfigString);
            var runtimeBootstrap = LoggingRuntimeBootstrap.Bootstrap(
                jsonConfig,
                new ConfiguredLogLevelRegistryFactory(),
                new DefaultLoggingConfigurationMapper());
            var newConfig = runtimeBootstrap.RuntimeConfiguration;

            // Decision table: level-only → sinks-only → reconstruct + swap.
            // A miss on any earlier branch falls through to the next; the
            // slow path is always available. ConfigDiff is null on the
            // first reload (no _currentConfig to compare against), in
            // which case we go straight to the slow path.
            ConfigDiff? diff = _currentConfig is null
                ? null
                : ConfigDiffDetector.Detect(_currentConfig, newConfig);

            if (TryLevelOnlyReload(diff, newConfig, runtimeBootstrap))
            {
                sw.Stop();
                OnReloadCompleted?.Invoke(new ReloadDiagnostics(
                    HotReloadOutcome.Applied, sourcePath, sw.ElapsedMilliseconds));
                return;
            }

            if (TrySinksOnlyReload(diff, jsonConfig, runtimeBootstrap, newConfig))
            {
                sw.Stop();
                OnReloadCompleted?.Invoke(new ReloadDiagnostics(
                    HotReloadOutcome.Applied, sourcePath, sw.ElapsedMilliseconds));
                return;
            }

            ReconstructAndSwap(jsonConfig, runtimeBootstrap, newConfig);
            sw.Stop();
            OnReloadCompleted?.Invoke(new ReloadDiagnostics(
                HotReloadOutcome.Applied, sourcePath, sw.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            sw.Stop();
            OnReloadFailed?.Invoke(new ReloadDiagnostics(
                HotReloadOutcome.Applied, sourcePath, sw.ElapsedMilliseconds, ex));
            throw;
        }
    }

    // Level-only fast path: only the minimum level changed and the live
    // pipeline has a global switch we can twist. Returns true when the
    // fast path applied; false to defer to the next branch.
    private bool TryLevelOnlyReload(
        ConfigDiff? diff,
        LoggingRuntimeConfiguration newConfig,
        LoggingRuntimeBootstrapResult runtimeBootstrap)
    {
        if (diff is null || !diff.IsLevelOnly) return false;
        if (_currentGlobalSwitch is null) return false;
        if (newConfig.PipelinePolicy.DynamicLevelPolicy is null) return false;

        var newMin = newConfig.PipelinePolicy.DynamicLevelPolicy.GlobalLevelSwitch.MinimumLevel;
        _currentGlobalSwitch.MinimumLevel = newMin;

        // Recompute the per-known-level accept booleans on the outer
        // StructuredLogger so source-gen-emitted code reading
        // IsDebugAcceptable / IsInfoAcceptable / etc. sees the new
        // minimum. Without this the IsXxxAcceptable values are pinned to
        // the construction-time minimum and a level-only reload that
        // lowers the floor silently keeps dropping events that should
        // now be accepted.
        _structuredLogger?.RecomputeAcceptables(newMin);

        _currentConfig = newConfig;
        return true;
    }

    // Sinks-only delta path: only per-sink properties changed. The live
    // pipeline's decorator chain, async queue, WAL, dynamic-level
    // switch, and enrichers stay valid; only the sink writers and the
    // kernel need to swap. Returns true on success; false to defer to
    // the reconstruct path.
    private bool TrySinksOnlyReload(
        ConfigDiff? diff,
        Configuration.Json.JsonLoggingConfig jsonConfig,
        LoggingRuntimeBootstrapResult runtimeBootstrap,
        LoggingRuntimeConfiguration newConfig)
    {
        if (diff is null || !diff.IsSinkPropertyOnly) return false;
        if (_currentPipelineAccessor is null) return false;

        var existingComposite = _currentPipelineAccessor.Get<SafeCompositeLogger>();
        if (existingComposite is null) return false;

        return TryExecuteSinksOnlyReload(jsonConfig, runtimeBootstrap, newConfig, existingComposite);
    }

    // Slow path: rebuild the full inner pipeline and swap. Always
    // applicable as a fallback for any change the fast paths cannot
    // express. Renamed from the pre-refactor ExecuteReload tail to
    // make the role explicit.
    private void ReconstructAndSwap(
        Configuration.Json.JsonLoggingConfig jsonConfig,
        LoggingRuntimeBootstrapResult runtimeBootstrap,
        LoggingRuntimeConfiguration newConfig)
    {
        // Reconstruct the enricher chain from the JSON config. Each entry
        // is resolved through EnricherJsonRegistry, so plugin enrichers
        // that registered their kind at init are restored along with the
        // built-ins. Multiple entries collapse into a CompositeLogEnricher
        // matching what QuickLogBuilder.EnricherSet.Resolve(...) produces
        // from the fluent API, so a Reload yields the same enrichment
        // behaviour as the original BuildAndCommit.
        var reconstructedEnricher = ReconstructEnrichers(jsonConfig.Enrichers);

        var reconstructedStrategy = PipelineStrategy.Resolve(
            jsonConfig.PipelineStrategyName,
            ExtractStepNames(jsonConfig.PipelineSteps));

        var reconstructedDecorators = ReconstructDecorators(jsonConfig.PipelineDecorators);

        var bootstrap = JsonConfiguredLoggingBootstrapFactory.Create(
            dateTimeProvider: _dateTimeProvider,
            runtimeConfiguration: runtimeBootstrap.RuntimeConfiguration,
            levelRegistry: runtimeBootstrap.LevelRegistry,
            hostAdapters: _hostAdapters,
            destructuringPolicies: _destructuringPolicies,
            additionalSinkProviders: _additionalSinkProviders,
            enricher: reconstructedEnricher,
            pipelineStrategy: reconstructedStrategy,
            customDecorators: reconstructedDecorators);

        var reloadAccessor = new Pipeline.PipelineAccessor();
        var result = bootstrap.Bootstrap(_defaultContext, pipelineAccessor: reloadAccessor);

        var newInnerPipeline = result.SwappableLogger?.Current
            ?? throw new InvalidOperationException(
                "Hot reload rebuild did not produce a SwappableLogger. " +
                "Ensure HotReload is enabled in the new configuration.");

        _swappableLogger.SwapInner(newInnerPipeline);

        // Swap the kernel-fast-path delegate too. StructuredLogger caches
        // a direct kernel reference to skip chain traversal for the
        // common case; that cache is set once at construction and points
        // at the original pipeline's kernel. Without this swap, every
        // Log call hits the OLD pipeline's kernel past the
        // SwappableLogger's swap and events land in an orphaned routing
        // graph that no subscriber reads from.
        _structuredLogger?.SwapKernel(result.Logger.KernelOrNull);

        ReinstallFastPathCompanions(jsonConfig, runtimeBootstrap.LevelRegistry, reloadAccessor);
        ReinstallNamingPolicy(jsonConfig);

        var oldResource = _currentAsyncResource;
        _currentAsyncResource = result.AsyncResource;
        _currentGlobalSwitch = result.DynamicLevelPolicy?.GlobalLevelSwitch;
        _currentConfig = newConfig;
        _currentLevelRegistry = runtimeBootstrap.LevelRegistry;
        _currentPipelineAccessor = reloadAccessor;

        if (oldResource is not null)
        {
            _janitor.Schedule(oldResource);
        }
    }

    // Sinks-only delta path. Builds a fresh pipeline so the existing
    // factory machinery resolves new sink kinds + properties correctly,
    // then keeps only the new sinks and the new kernel — discarding the
    // new pipeline's chain, async queue, WAL, etc., because the live
    // pipeline's instances of those didn't need to change.
    //
    // Returns false on any unexpected shape (kernel mismatch, no
    // SafeCompositeLogger in the new accessor, factory throw). The
    // caller falls back to the slow rebuild on false. The fall-back is
    // deliberately quiet; the engine prefers correct-and-slow over a
    // half-applied delta.
    //
    // Cognitive Complexity: the helper is intentionally linear — build,
    // extract, swap children, swap kernel, schedule disposals.
    private bool TryExecuteSinksOnlyReload(
        Configuration.Json.JsonLoggingConfig jsonConfig,
        Configuration.LoggingRuntimeBootstrapResult runtimeBootstrap,
        LoggingRuntimeConfiguration newConfig,
        SafeCompositeLogger existingComposite)
    {
        // Captured outside the try so the catch block can route any
        // partially-built async resource through the janitor instead of
        // leaking it. newResult is null until Bootstrap() returns.
        Bootstrap.LoggingBootstrapResult? newResult = null;
        try
        {
            var reconstructedEnricher = ReconstructEnrichers(jsonConfig.Enrichers);
            var reconstructedStrategy = PipelineStrategy.Resolve(
                jsonConfig.PipelineStrategyName,
                ExtractStepNames(jsonConfig.PipelineSteps));
            var reconstructedDecorators = ReconstructDecorators(jsonConfig.PipelineDecorators);

            var bootstrap = JsonConfiguredLoggingBootstrapFactory.Create(
                dateTimeProvider: _dateTimeProvider,
                runtimeConfiguration: runtimeBootstrap.RuntimeConfiguration,
                levelRegistry: runtimeBootstrap.LevelRegistry,
                hostAdapters: _hostAdapters,
                destructuringPolicies: _destructuringPolicies,
                additionalSinkProviders: _additionalSinkProviders,
                enricher: reconstructedEnricher,
                pipelineStrategy: reconstructedStrategy,
                customDecorators: reconstructedDecorators);

            var newAccessor = new Pipeline.PipelineAccessor();
            newResult = bootstrap.Bootstrap(
                _defaultContext,
                pipelineAccessor: newAccessor);

            var newComposite = newAccessor.Get<SafeCompositeLogger>();
            if (newComposite is null)
            {
                // The new pipeline doesn't expose a SafeCompositeLogger
                // (atypical strategy). Slow rebuild handles this shape.
                ScheduleNewResultDisposalIfPresent(newResult);
                return false;
            }

            // Swap sinks first so events on the chain path land at the new
            // sinks immediately. Kernel callers still see the old kernel
            // (and old sinks) for the next nanoseconds — both paths reach
            // a coherent set of sinks per call.
            var newSinks = newComposite.Children;
            var oldSinks = existingComposite.SwapChildren(newSinks);

            // Swap the kernel so kernel-eligible callers also see new sinks.
            // The new pipeline's structured logger built a kernel against
            // the new sinks; we lift just that kernel and discard the rest.
            _structuredLogger?.SwapKernel(newResult.Logger.KernelOrNull);

            _currentConfig = newConfig;
            _currentLevelRegistry = runtimeBootstrap.LevelRegistry;
            // Keep _currentAsyncResource pointing at the LIVE pipeline's
            // async resource — we did not swap the chain. The new
            // pipeline's async resource is unused and gets disposed below.

            // Dispose the old per-sink writers and the unused new pipeline
            // resources. Old sinks may be IAsyncDisposable (queue-backed)
            // or IDisposable (file handle); both shapes route through the
            // janitor's bounded-dispose surface.
            DisposeOldSinks(oldSinks);
            ScheduleNewResultDisposalIfPresent(newResult);

            return true;
        }
        catch (Exception ex)
        {
            // Surface the failure so an operator who edited their config
            // can see why we fell back, then return false so the slow
            // rebuild runs and applies the change correctly. Route any
            // partially-built async resource through the janitor so an
            // exception after Bootstrap() returned does not leak.
            ScheduleNewResultDisposalIfPresent(newResult);
            _janitor.Report(ex, "HotReloadSinksOnlyDeltaFailed");
            return false;
        }
    }

    // Single seam every TryExecuteSinksOnlyReload exit goes through to
    // dispose the unused new-pipeline resources. Centralising the call
    // means a future addition to LoggingBootstrapResult (a new
    // IAsyncDisposable beyond AsyncResource) only needs editing here, and
    // the catch / early-exit branches can never forget to schedule.
    private void ScheduleNewResultDisposalIfPresent(Bootstrap.LoggingBootstrapResult? newResult)
    {
        if (newResult?.AsyncResource is { } resource)
        {
            _janitor.Schedule(resource);
        }
    }

    // Schedule each old sink writer for disposal through the janitor so a
    // stuck shutdown surfaces as telemetry instead of a leaked file
    // handle or socket. Sinks that are neither IAsyncDisposable nor
    // IDisposable are silently skipped — they have no resources to
    // release.
    private void DisposeOldSinks(IReadOnlyList<ILogger> oldSinks)
    {
        foreach (var sink in oldSinks)
        {
            switch (sink)
            {
                case IAsyncDisposable asyncDisposable:
                    _janitor.Schedule(asyncDisposable);
                    break;
                case IDisposable syncDisposable:
                    try { syncDisposable.Dispose(); }
                    catch (Exception ex) { _janitor.Report(ex, "OldSinkDisposalFailed"); }
                    break;
            }
        }
    }

    // Default timeout exposed as a static readonly so the constructor can
    // fall back when the caller passes null. The janitor itself captures
    // the per-instance timeout at construction time so two bootstraps
    // cannot share the knob.
    private static readonly TimeSpan DefaultOldResourceDisposeTimeout = TimeSpan.FromSeconds(30);

    // Backwards-compatible wrapper kept for the drain-cap and
    // dispose-with-pending paths that already call ReportDisposalFailure.
    // Forwards to the janitor's Report shape.
    private void ReportDisposalFailure(Exception ex, string reason) =>
        _janitor.Report(ex, reason);

    // Pulls just the step names out of the JSON pipeline-steps array.
    // Resolve() needs the names to drive PipelineStrategy.FromNames in the
    // "custom" branch; named presets ignore the array.
    private static IReadOnlyList<string>? ExtractStepNames(
        IReadOnlyList<Configuration.Json.JsonPipelineStepConfig>? steps)
    {
        if (steps is null || steps.Count == 0) return null;
        var names = new string[steps.Count];
        for (var i = 0; i < steps.Count; i++)
        {
            names[i] = steps[i].StepName;
        }
        return names;
    }

    // Reconstructs the custom-decorator chain through DecoratorJsonRegistry.
    // Returns null when no decorators are declared so the factory's
    // explicit-null path stays the default ("no custom decorators").
    private static IReadOnlyList<Pipeline.IConfigurablePipelineDecorator>? ReconstructDecorators(
        IReadOnlyList<Configuration.Json.JsonPipelineDecoratorConfig>? decorators)
    {
        if (decorators is null || decorators.Count == 0) return null;

        var resolved = new Pipeline.IConfigurablePipelineDecorator[decorators.Count];
        for (var i = 0; i < decorators.Count; i++)
        {
            resolved[i] = Pipeline.DecoratorJsonRegistry.Reconstruct(decorators[i]);
        }
        return resolved;
    }

    private static Enrichers.ILogEnricher? ReconstructEnrichers(
        IReadOnlyList<Configuration.Json.JsonEnricherConfig>? enrichers)
    {
        if (enrichers is null || enrichers.Count == 0) return null;

        if (enrichers.Count == 1)
        {
            return Enrichers.EnricherJsonRegistry.Reconstruct(enrichers[0]);
        }

        var resolved = new Enrichers.ILogEnricher[enrichers.Count];
        for (var i = 0; i < enrichers.Count; i++)
        {
            resolved[i] = Enrichers.EnricherJsonRegistry.Reconstruct(enrichers[i]);
        }
        return new Enrichers.CompositeLogEnricher(resolved);
    }

    /// <summary>
    /// Re-install kernel-aware fast-path companions on the outer
    /// StructuredLogger from the JSON config. Each Install* call also
    /// accepts <c>null</c>, which clears any previously-installed
    /// companion — a config that drops a fast-path section between
    /// reloads correctly removes the companion at runtime.
    ///
    /// <para>
    /// Called once per Reload, after the inner pipeline + kernel have
    /// already been swapped. JSON is the source of truth: any runtime
    /// mutation made between reloads (e.g. setting
    /// <c>levelSwitch.MinimumLevel</c> directly) is overwritten by the
    /// JSON's value here. That matches the legacy DynamicLevelPolicy
    /// reload behaviour for slow-path reloads.
    /// </para>
    /// </summary>
    /// <summary>
    /// Hot-reload-aware naming-policy install. When the JSON config names
    /// a registered policy id, swap to it. When it names an UNKNOWN id,
    /// keep the currently-active policy and surface a failure-sink event
    /// describing the degradation — Phase 5 Will subscribe more
    /// observability on this surface. When the field is omitted, fall back
    /// to the spec default (Pascal).
    ///
    /// <para>
    /// Differs deliberately from the cold-start path
    /// (<c>QuickLogBuilder.FromConfiguration</c>), which throws
    /// <c>UnknownNamingPolicyException</c> on unknown id. Hot-reload must
    /// not crash a running pipeline; cold-start is allowed to fail loud
    /// because the host hasn't started serving traffic yet.
    /// </para>
    /// </summary>
    private void ReinstallNamingPolicy(Configuration.Json.JsonLoggingConfig jsonConfig)
    {
        if (_structuredLogger is null) return;

        var newPolicyId = jsonConfig.NamingPolicy;

        // Omitted field → spec default. Reload back to PascalCasePolicy so a
        // JSON that previously named "snake" but was edited to drop the
        // field round-trips to the default rather than keeping the prior
        // override silently in place.
        if (string.IsNullOrEmpty(newPolicyId))
        {
            _structuredLogger.InstallNamingPolicy(Templating.PropertyNamingPolicy.Pascal);
            return;
        }

        if (Templating.NamingPolicyRegistry.TryResolve(newPolicyId, out var resolved) && resolved is not null)
        {
            _structuredLogger.InstallNamingPolicy(resolved);
            return;
        }

        // Unknown policy id during a hot-reload. Degrade: keep the
        // currently-active policy, surface the failure to operators via
        // the failure sink. We construct a small diagnostic-only
        // exception to satisfy the non-nullable parameter; the LogEvent
        // carries the human-readable message.
        var current = _structuredLogger.NamingPolicy;
        var reason = new Templating.UnknownNamingPolicyException(newPolicyId);
        _failureSink?.ReportFailure(
            new LogEvent(
                DateTimeOffset.UtcNow,
                KnownLogLevels.Warn,
                LogCategory.App,
                "Hot reload kept naming policy {Kept} because requested policy {Requested} is not registered.",
                $"Hot reload kept naming policy {current.Id} because requested policy '{newPolicyId}' is not registered.",
                LogEvent.EmptyProperties,
                LogEvent.EmptyContext),
            reason,
            nameof(HotReloadableLoggingBootstrap));
    }

    private void ReinstallFastPathCompanions(
        Configuration.Json.JsonLoggingConfig jsonConfig,
        ILogLevelRegistry levelRegistry,
        Pipeline.PipelineAccessor? newAccessor)
    {
        if (_structuredLogger is null) return;

        // Redaction
        if (jsonConfig.FastPathRedaction is { Rules.Count: > 0 } redactCfg)
        {
            var rules = new Pipeline.Processors.CompiledRedactionRule[redactCfg.Rules.Count];
            for (var i = 0; i < redactCfg.Rules.Count; i++)
            {
                var entry = redactCfg.Rules[i];
                rules[i] = new Pipeline.Processors.CompiledRedactionRule(
                    PropertyNamePattern: entry.PropertyName,
                    Mode: ParseRedactionMode(entry.Mode),
                    MaskChar: entry.MaskChar,
                    VisibleChars: entry.VisibleChars);
            }
            _structuredLogger.InstallFastPathRedactor(
                new Pipeline.Kernel.FastPathRedactor(rules));
        }
        else
        {
            _structuredLogger.InstallFastPathRedactor(null);
        }

        // Sampling
        _structuredLogger.InstallFastPathSampler(
            jsonConfig.FastPathSampling is { } sampleCfg
                ? new Pipeline.Kernel.FastPathSampler(sampleCfg.SampleRate)
                : null);

        // Static enrichment
        if (jsonConfig.FastPathEnrichment is { Properties.Count: > 0 } enrichCfg)
        {
            var props = new Templating.LogProperty[enrichCfg.Properties.Count];
            for (var i = 0; i < enrichCfg.Properties.Count; i++)
            {
                var entry = enrichCfg.Properties[i];
                props[i] = new Templating.LogProperty(entry.Name, entry.Value);
            }
            _structuredLogger.InstallFastPathEnricher(
                new Pipeline.Kernel.FastPathEnricher(props));
        }
        else
        {
            _structuredLogger.InstallFastPathEnricher(null);
        }

        // Dynamic level
        if (jsonConfig.FastPathDynamicLevel is { } dynCfg
            && levelRegistry.GetByKeyOrNull(dynCfg.InitialLevel) is { } initialLevel)
        {
            var levelSwitch = new LogLevelSwitch(initialLevel);

            // Per-category override map: build only when the JSON carries
            // entries; otherwise stay null so the resolver branches to the
            // global-only path. Categories whose level key is unknown are
            // skipped silently — same forgiveness model the global level
            // path uses (an unknown global key falls into the else branch
            // and clears the resolver entirely; an unknown category key
            // is a partial-config issue and should not erase the working
            // global switch).
            Levels.CategoryLevelSwitchMap? categoryMap = null;
            if (dynCfg.Categories is { Count: > 0 } categories)
            {
                categoryMap = new Levels.CategoryLevelSwitchMap();
                foreach (var entry in categories)
                {
                    if (levelRegistry.GetByKeyOrNull(entry.Value) is { } categoryLevel)
                    {
                        categoryMap.SetCategoryLevel(entry.Key, categoryLevel);
                    }
                }
            }

            _structuredLogger.InstallFastPathDynamicLevel(
                new Pipeline.Kernel.FastPathDynamicLevel(levelSwitch, categoryMap, levelRegistry));
        }
        else
        {
            _structuredLogger.InstallFastPathDynamicLevel(null);
        }

        // Async sink wrapper. Install delegates to the same helper Build
        // uses (Quick.QuickLogBuilder.InstallFastPathAsyncSinkWrapper),
        // which atomically swaps the kernel and retires the prior
        // wrapper off-thread. When the JSON drops the section, clear
        // the slot so a previously-async pipeline reverts to the direct
        // sink fan-out the rebuilt kernel already supplies.
        if (jsonConfig.FastPathAsyncSink is { BoundedCapacity: > 0 } asyncCfg && newAccessor is not null)
        {
            Quick.QuickLogBuilder.InstallFastPathAsyncSinkWrapper(
                _structuredLogger, newAccessor, asyncCfg.BoundedCapacity);
        }
        else
        {
            // No async wrapper in the new config — drain + dispose any
            // prior wrapper. The kernel was already swapped above to the
            // pipeline's direct fan-out, so post-swap events route there
            // straight; we only need to retire the old wrapper. Route
            // through the janitor so every reload disposal exits through
            // one shape (principal-review queue #14) — the prior inline
            // Task.Run swallowed exceptions and skipped the bounded
            // timeout the janitor enforces.
            var prior = _structuredLogger.InstallFastPathAsyncSink(null);
            if (prior is not null)
            {
                _janitor.Schedule(prior);
            }
        }
    }

    private static Output.Rendering.RedactionMode ParseRedactionMode(string mode) =>
        mode.ToLowerInvariant() switch
        {
            "remove" => Output.Rendering.RedactionMode.Remove,
            "mask" => Output.Rendering.RedactionMode.Mask,
            "hash" => Output.Rendering.RedactionMode.Hash,
            _ => throw new System.ArgumentException(
                $"Unknown fast-path redaction mode '{mode}'. " +
                "Expected 'remove', 'mask', or 'hash'.",
                nameof(mode)),
        };

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _reloadSource?.Dispose();

        // A pending JSON at dispose time is intentionally abandoned. Surface
        // through the failure sink so an operator who sees their last-edit
        // didn't apply has a record of why. Failing silently here is the
        // pre-fix shape — the seam was that no one knew an edit had been
        // queued during the busy lock and never drained.
        if (TakePendingJson() is not null)
        {
            ReportDisposalFailure(
                new InvalidOperationException(
                    "Hot reload disposed with a pending reload still queued; the most-recent JSON was not applied."),
                "HotReloadDisposedWithPending");
        }

        _reloadLock.Dispose();
    }
}
