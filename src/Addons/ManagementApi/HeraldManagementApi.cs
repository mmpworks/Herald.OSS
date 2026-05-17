#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MMP.Herald.Configuration;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Filters;
using MMP.Herald.Levels;
using MMP.Herald.Metrics;
using MMP.Herald.Pipeline;
using MMP.Herald.Quick;

namespace MMP.Herald.Addons.ManagementApi;

/// <summary>
/// Pure C# management API for Herald pipelines. No HTTP dependency.
/// Wraps QuickLogBuilder + QuickLogResult and exposes every configurable
/// property as an operation. Supports transactional reprogramming:
/// Begin() → make changes → Commit() or Rollback().
///
/// This API is the single surface that HTTP connectors, game consoles,
/// test harnesses, and CLI tools consume. The HTTP layer in Herald.Dashboard
/// maps these operations to REST endpoints.
///
/// Usage:
///   var api = new HeraldManagementApi(builder, result);
///   api.BeginTransaction();
///   api.SetMinimumLevel("debug");
///   api.SetPipelineStrategy("filterEarly");
///   api.CommitTransaction(); // atomic swap
/// </summary>
public sealed class HeraldManagementApi
{
    private readonly QuickLogBuilder _builder;
    private QuickLogResult _result;
    private readonly HeraldRegistration? _registration;
    private string? _snapshot; // JSON snapshot for rollback
    private bool _inTransaction;

    public HeraldManagementApi(QuickLogBuilder builder, QuickLogResult result)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _result = result ?? throw new ArgumentNullException(nameof(result));
    }

    /// <summary>
    /// Create a management API from a registry entry, inheriting its ConfigPath
    /// so commits auto-persist for persistent pipelines.
    /// </summary>
    public static HeraldManagementApi FromRegistration(HeraldRegistration entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new HeraldManagementApi(entry.Builder, entry.Result, entry) { ConfigPath = entry.ConfigPath };
    }

    private HeraldManagementApi(QuickLogBuilder builder, QuickLogResult result, HeraldRegistration? registration)
        : this(builder, result)
    {
        _registration = registration;
    }

    /// <summary>
    /// When set, the pipeline config is persisted to this file path after every
    /// successful commit (both auto-commit and transactional commit).
    /// </summary>
    public string? ConfigPath { get; set; }

    // ── Read Operations ──────────────────────────────────────────────

    /// <summary>Current configuration as JSON.</summary>
    public string GetConfigJson() => _builder.ExportConfig();

    /// <summary>Builder inspection snapshot as a serializable object.</summary>
    public BuilderInspection GetInspection() => _builder.Inspect();

    /// <summary>Validation result for the current builder state.</summary>
    public ValidationResult GetValidation() => _builder.Validate();

    /// <summary>Multi-line diagnostic dump of the live pipeline.</summary>
    public string GetDiagnosticDump() => _result.DiagnosticDump();

    /// <summary>Pipeline component types currently registered.</summary>
    public IReadOnlyList<string> GetPipelineComponents() =>
        _result.Pipeline.ComponentTypes.Select(t => t.Name).ToList();

    /// <summary>Pipeline component names (plugin/custom registrations).</summary>
    public IReadOnlyList<string> GetPipelineComponentNames() =>
        _result.Pipeline.ComponentNames.ToList();

    /// <summary>
    /// Returns the pipeline flow: the ordered list of strategy steps,
    /// with display names and descriptions for dashboard rendering.
    /// Also includes the list of sinks under the FanOut step.
    /// </summary>
    public PipelineFlowInfo GetPipelineFlow()
    {
        var inspection = _builder.Inspect();
        var strategy = inspection.PipelineStrategy ?? Configuration.PipelineStrategy.Default();
        var steps = new List<PipelineFlowStep>();

        foreach (var step in strategy.Steps)
        {
            steps.Add(new PipelineFlowStep(
                StepName: step.Name,
                Alias: _builder.GetAlias(step.Name)));
        }

        var sinks = new List<PipelineFlowSink>();
        if (inspection.HasConsoleSink)
        {
            var known = Configuration.KnownSink.FromKind(Services.KnownSinkKinds.Console);
            sinks.Add(new PipelineFlowSink(Services.KnownSinkKinds.Console,
                known?.DisplayName ?? "Console Sink", known?.Description ?? "Writes to stdout",
                inspection.ConsoleMinLevel, Help: known?.Help ?? "", Vendor: known?.Vendor,
                Alias: _builder.GetAlias("console"),
                Schema: known?.ConfigurationSchema is { Count: > 0 } s1 ? s1 : null));
        }
        if (inspection.HasFileSink)
        {
            var fileKind = inspection.FileKind ?? Services.KnownSinkKinds.TextFile;
            var known = Configuration.KnownSink.FromKind(fileKind);
            var fileConfig = BuildFileSinkConfig(inspection);
            sinks.Add(new PipelineFlowSink(fileKind,
                known?.DisplayName ?? "File Sink", known?.Description ?? ("Writes to " + (inspection.FilePath ?? "disk")),
                inspection.FileMinLevel, Help: known?.Help ?? "", Vendor: known?.Vendor,
                Alias: _builder.GetAlias("file"),
                Schema: known?.ConfigurationSchema is { Count: > 0 } s2 ? s2 : null,
                Config: fileConfig,
                ConfigContract: ReadConfigContract(fileKind)));
        }
        foreach (var ch in inspection.ChannelNames)
        {
            var known = Configuration.KnownSink.FromKind(Services.KnownSinkKinds.Channel);
            sinks.Add(new PipelineFlowSink("channel:" + ch,
                known?.DisplayName != null ? known.DisplayName + ": " + ch : "Channel: " + ch,
                known?.Description ?? "Named channel sink", null, Help: known?.Help ?? "", Vendor: known?.Vendor,
                Alias: _builder.GetAlias("channel:" + ch),
                Schema: known?.ConfigurationSchema is { Count: > 0 } s3 ? s3 : null));
        }
        for (var i = 0; i < inspection.BridgeCount; i++)
        {
            var known = Configuration.KnownSink.FromKind(Services.KnownSinkKinds.PipelineBridge);
            sinks.Add(new PipelineFlowSink(Services.KnownSinkKinds.PipelineBridge + ":" + i,
                known?.DisplayName != null ? known.DisplayName + " " + i : "Bridge " + i,
                known?.Description ?? "Pipeline bridge", null, Help: known?.Help ?? "", Vendor: known?.Vendor,
                Alias: _builder.GetAlias("bridge:" + i),
                Schema: known?.ConfigurationSchema is { Count: > 0 } s4 ? s4 : null));
        }
        // Custom sink-provider kinds are deliberately NOT emitted as flow
        // sinks. They are runtime provider registrations attached via
        // builder.WithCustomSinkProvider — not operator-configurable sinks.
        // Treating them as flow rows produced phantom entries in the
        // dashboard pipeline editor (e.g. an `text_file (red)` row with no
        // path) that operators interpreted as broken sinks. The provider's
        // contribution shows up through whichever real sink-config row uses
        // its kind (the JsonLogSinkConfig entry the per-kind serializer
        // emits). Operators who need visibility into which providers are
        // attached have BuilderInspection.CustomSinkProviderKinds and the
        // /api/inspection endpoint.

        return new PipelineFlowInfo(
            Steps: steps,
            Sinks: sinks,
            TestLoopbackUrl: _builder.TestLoopbackUrl,
            TestLoopbackLogDir: _builder.TestLoopbackLogDir,
            LoopbackEntriesPerFile: _builder.LoopbackEntriesPerFile,
            LoopbackUseNdjson: _builder.LoopbackUseNdjson);
    }

    /// <summary>
    /// All registered pipeline step types. Global - not per-instance.
    /// Used by the dashboard's Available Pipelines panel.
    /// </summary>
    public static IReadOnlyList<PipelineFlowStep> GetAllKnownSteps()
    {
        var steps = new List<PipelineFlowStep>();
        foreach (var name in Configuration.PipelineStep.AllNames)
        {
            var step = Configuration.PipelineStep.FromName(name);
            if (step is not null)
            {
                steps.Add(new PipelineFlowStep(
                    StepName: step.Name,
                    DisplayName: step.DisplayName,
                    Description: step.Description,
                    Help: step.Help,
                    Vendor: step.Vendor,
                    LinkType: step.LinkType));
            }
        }
        return steps;
    }

    /// <summary>
    /// All registered sink types. Global - not per-instance.
    /// Used by the dashboard's Available Sinks panel.
    /// </summary>
    public static IReadOnlyList<PipelineFlowSink> GetAllKnownSinks()
    {
        var sinks = new List<PipelineFlowSink>();
        foreach (var kind in Configuration.KnownSink.AllKinds)
        {
            var sink = Configuration.KnownSink.FromKind(kind);
            if (sink is not null)
            {
                sinks.Add(new PipelineFlowSink(
                    SinkId: sink.Kind,
                    DisplayName: sink.DisplayName,
                    Description: sink.Description,
                    MinLevel: null,
                    Help: sink.Help,
                    Vendor: sink.Vendor,
                    Schema: sink.ConfigurationSchema.Count > 0 ? sink.ConfigurationSchema : null));
            }
        }
        return sinks;
    }

    /// <summary>
    /// Generate a proposed configuration JSON without modifying the live pipeline.
    /// Applies the proposed changes to the builder, calls Build() to produce the
    /// real config, then returns the JSON. The live pipeline is NOT swapped.
    /// Changes to the builder are real but uncommitted - call Commit to activate.
    /// </summary>
    public string GenerateProposal(
        IReadOnlyList<string>? proposedSteps = null,
        IReadOnlyDictionary<string, string>? proposedAliases = null)
    {
        // Apply proposed strategy if provided
        if (proposedSteps is not null)
        {
            var strategy = Configuration.PipelineStrategy.FromNames(proposedSteps);
            _builder.WithPipelineStrategy(strategy);
        }

        // Apply proposed aliases if provided
        if (proposedAliases is not null)
        {
            foreach (var kvp in proposedAliases)
                _builder.WithAlias(kvp.Key, kvp.Value);
        }

        // Build() produces the full config JSON without making it live
        var buildResult = _builder.Build();
        return buildResult.ExportConfig();
    }

    /// <summary>
    /// Build the current builder state and commit it to the live pipeline.
    /// Returns the config JSON that was committed.
    /// </summary>
    public string BuildAndCommit()
    {
        var buildResult = _builder.Build();
        _result.Commit(buildResult);
        return buildResult.ExportConfig();
    }

    /// <summary>Runtime pipeline state: queue depth, circuit breaker, health.</summary>
    public PipelineRuntimeState GetRuntimeState()
    {
        var asyncLogger = _result.Pipeline.Get<AsyncLogger>();
        // Query the circuit-breaker via its Core-level interface so this
        // method compiles when CircuitBreakerLogger physically lives in the
        // Herald.Pro plugin assembly. Returns null when the Pro plugin
        // isn't loaded — handled below by the null-conditional access.
        var circuitBreaker = _result.Pipeline.Get<ICircuitBreakerRuntimeState>();
        var filtering = _result.Pipeline.Get<FilteringLogger>();
        var composite = _result.Pipeline.Get<SafeCompositeLogger>();

        return new PipelineRuntimeState(
            AsyncQueueDepth: asyncLogger?.QueueDepth,
            AsyncCapacity: asyncLogger?.Capacity,
            AsyncDropStrategy: asyncLogger?.DropStrategy,
            CircuitBreakerState: circuitBreaker?.CurrentState switch
            {
                0 => "Closed",
                1 => "Open",
                2 => "HalfOpen",
                _ => null
            },
            CircuitBreakerFailures: circuitBreaker?.ConsecutiveFailures,
            FilterCount: filtering?.Filters.Count,
            SinkCount: composite?.ChildCount,
            IsTransactionActive: _inTransaction);
    }

    // ── Container Step Children ─────────────────────────────────────

    /// <summary>
    /// Returns child items for all container steps in one call.
    /// Keys are step names (e.g. "eventProcessing").
    /// FanOut sinks are handled separately via GetPipelineFlow().
    /// Filtering is a config-only step (level shown in its schema), not a container.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PipelineChildInfo>> GetStepChildren()
    {
        var result = new Dictionary<string, IReadOnlyList<PipelineChildInfo>>();

        var processors = GetProcessors();
        if (processors.Count > 0) result["eventProcessing"] = processors;

        return result;
    }

    /// <summary>
    /// Returns the individual processors inside the EventProcessingLogger.
    /// </summary>
    public IReadOnlyList<PipelineChildInfo> GetProcessors()
    {
        var processing = _result.Pipeline.Get<Pipeline.EventProcessingLogger>();
        if (processing is null) return [];

        var results = new List<PipelineChildInfo>();
        foreach (var proc in processing.Processors)
        {
            var typeName = proc.GetType().Name;
            var displayName = typeName switch
            {
                "CompiledRedactionProcessor" => "Compiled Redaction",
                "LogMetricExtractor" => "Metric Extractor",
                "LogDeduplicationProcessor" => "Deduplication",
                "SentenceLogDetector" => "Sentence Log Detector",
                "LogSchemaRegistry" => "Schema Validator",
                "StrategyValidator" => "Strategy Validator",
                "ErrorBudgetMonitor" => "Error Budget Monitor",
                "CardinalityGuardProcessor" => "Cardinality Guard",
                _ => typeName
            };
            results.Add(new PipelineChildInfo(typeName, displayName, typeName, Icon: "tune"));
        }
        return results;
    }

    // ── Plugin Sink Discovery & Configuration ─────────────────────────

    /// <summary>
    /// Discover all registered sink providers. Returns basic info for all providers,
    /// plus full configuration schema for those implementing IConfigurableSinkProvider.
    /// The dashboard uses this to render plugin configuration forms dynamically.
    /// </summary>
    public IReadOnlyList<SinkProviderInfo> GetSinkProviders()
    {
        var results = new List<SinkProviderInfo>();
        foreach (var reg in _builder.SinkProviders.Items)
        {
            if (reg.Value is Routing.IConfigurableSinkProvider configurable)
            {
                results.Add(new SinkProviderInfo(
                    SinkKind: configurable.SinkKind,
                    DisplayName: configurable.DisplayName,
                    Description: configurable.Description,
                    IsConfigurable: true,
                    Schema: configurable.ConfigurationSchema,
                    CurrentConfig: configurable.GetConfiguration()));
            }
            else
            {
                results.Add(new SinkProviderInfo(
                    SinkKind: reg.Value.SinkKind,
                    DisplayName: reg.Value.SinkKind,
                    Description: null,
                    IsConfigurable: false,
                    Schema: null,
                    CurrentConfig: null));
            }
        }
        return results;
    }

    /// <summary>
    /// Get the configuration schema and current values for a specific sink provider.
    /// Returns null if the provider is not registered or not configurable.
    /// </summary>
    public SinkProviderInfo? GetSinkProviderConfig(string sinkKind)
    {
        var provider = _builder.SinkProviders.Get(sinkKind);
        if (provider is not Routing.IConfigurableSinkProvider configurable)
            return null;

        return new SinkProviderInfo(
            SinkKind: configurable.SinkKind,
            DisplayName: configurable.DisplayName,
            Description: configurable.Description,
            IsConfigurable: true,
            Schema: configurable.ConfigurationSchema,
            CurrentConfig: configurable.GetConfiguration());
    }

    /// <summary>
    /// Return the raw <c>CAPABILITY.yaml</c> manifest a sink provider's
    /// assembly ships with, or <c>null</c> when the provider is not
    /// registered or carries no embedded manifest.
    ///
    /// <para>The manifest text is intentionally returned as-is (no parse
    /// here) so Herald.Core stays free of a YAML parser dependency. The
    /// <c>Herald.ManagementApi</c> HTTP layer parses + caches + serves
    /// the JSON shape consumers actually use.</para>
    /// </summary>
    public string? GetSinkProviderCapabilityYaml(string sinkKind)
    {
        var provider = _builder.SinkProviders.Get(sinkKind);
        return provider?.GetCapabilityYaml();
    }

    /// <summary>
    /// Return the raw <c>configuration.mmpform</c> text a sink provider's
    /// assembly ships with, or <c>null</c> when no provider is registered
    /// or no form file is embedded. Mirrors <see cref="GetSinkProviderCapabilityYaml"/>.
    ///
    /// <para>Used by the management HTTP layer to populate the
    /// <c>formSchemaText</c> field on the capability response when a sink
    /// opts into the v2 form-schema path (<c>formSchema: configuration.mmpform</c>
    /// in CAPABILITY.yaml).</para>
    /// </summary>
    public string? GetSinkProviderFormSchemaText(string sinkKind)
    {
        var provider = _builder.SinkProviders.Get(sinkKind);
        return provider?.GetFormSchemaText();
    }

    /// <summary>
    /// Apply configuration values to a configurable sink provider.
    /// The provider validates the values and returns success/failure.
    /// </summary>
    public ManagementResult ConfigureSinkProvider(
        string sinkKind, IReadOnlyDictionary<string, object?> values)
    {
        var provider = _builder.SinkProviders.Get(sinkKind);
        if (provider is not Routing.IConfigurableSinkProvider configurable)
            return ManagementResult.Fail($"Sink provider '{sinkKind}' is not configurable or not registered.");

        var (success, error) = configurable.ApplyConfiguration(values);
        if (!success)
            return ManagementResult.Fail($"Configuration rejected by '{sinkKind}': {error}");

        return AutoCommitOrStage($"Sink provider '{sinkKind}' configured.");
    }

    // ── Plugin Pipeline Decorator Discovery & Configuration ──────────

    /// <summary>
    /// Discover all pipeline components with configuration schemas.
    /// Includes both plugin decorators (IConfigurablePipelineDecorator) and
    /// built-in components that implement IComponentMetadata with a non-empty schema.
    /// </summary>
    public IReadOnlyList<PipelineDecoratorInfo> GetPipelineDecorators()
    {
        var results = new List<PipelineDecoratorInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Plugin decorators first (they have mutable config)
        foreach (var d in _builder.PipelineDecorators)
        {
            seen.Add(d.StepName);
            results.Add(new PipelineDecoratorInfo(
                StepName: d.StepName,
                DisplayName: d.DisplayName,
                Description: d.Description,
                Schema: d.ConfigurationSchema,
                CurrentConfig: d.GetConfiguration()));
        }

        // Live pipeline components with IComponentMetadata — these have real runtime values
        foreach (var component in _result.Pipeline.AllComponents)
        {
            if (component is Pipeline.IComponentMetadata meta && !seen.Contains(meta.ComponentName))
            {
                var schema = meta.ConfigurationSchema;
                if (schema is not null && schema.Count > 0)
                {
                    var currentConfig = new Dictionary<string, object?>();
                    foreach (var field in schema)
                        currentConfig[field.Name] = field.DefaultValue;

                    seen.Add(meta.ComponentName);
                    results.Add(new PipelineDecoratorInfo(
                        StepName: meta.ComponentName,
                        DisplayName: meta.DisplayName,
                        Description: meta.Description,
                        Schema: schema,
                        CurrentConfig: currentConfig));
                }
            }
        }

        // Static schemas for steps in the current strategy that aren't instantiated
        // (e.g. Async is in the strategy but not enabled). Use the static schema
        // registry so the dashboard can still show what WOULD be configurable.
        var inspection = _builder.Inspect();
        var strategy = inspection.PipelineStrategy ?? Configuration.PipelineStrategy.Default();
        foreach (var step in strategy.Steps)
        {
            if (seen.Contains(step.Name)) continue;
            var schema = Pipeline.ComponentSchemaRegistry.GetSchema(step.Name);
            if (schema is not null && schema.Count > 0)
            {
                var currentConfig = new Dictionary<string, object?>();
                foreach (var field in schema)
                    currentConfig[field.Name] = field.DefaultValue;

                seen.Add(step.Name);
                results.Add(new PipelineDecoratorInfo(
                    StepName: step.Name,
                    DisplayName: step.DisplayName,
                    Description: step.Description,
                    Schema: schema,
                    CurrentConfig: currentConfig));
            }
        }

        return results;
    }

    /// <summary>Apply configuration to a pipeline decorator plugin.</summary>
    public ManagementResult ConfigurePipelineDecorator(
        string stepName, IReadOnlyDictionary<string, object?> values)
    {
        var decorator = _builder.GetPipelineDecorator(stepName);
        if (decorator is null)
            return ManagementResult.Fail($"Pipeline decorator '{stepName}' is not registered.");

        var (success, error) = decorator.ApplyConfiguration(values);
        if (!success)
            return ManagementResult.Fail($"Configuration rejected by '{stepName}': {error}");

        return AutoCommitOrStage($"Pipeline decorator '{stepName}' configured.");
    }

    // ── Full Commit ──────────────────────────────────────────────────

    /// <summary>
    /// Apply a complete pipeline configuration from the Dashboard.
    /// Receives steps (with configs), sinks (with configs), and aliases in one pass.
    // Single funnel for every per-sink runtime change the dashboard's
    // strip can fire (run state + tee flags + sink-level minimum).
    // Updates the in-memory holders so the next event respects the
    // new value, mirrors the change into the builder's
    // SinkRuntimeOverrides map, then persists the JSON so a reboot
    // sees the same state. CUPID + DRY: every endpoint and helper
    // that touches per-sink runtime routes through here, so there
    // is exactly one persistence path to reason about.
    public sealed record SinkRuntimeApplyResult(
        ManagementResult Result,
        string? PreviousRunState,
        string? RunState,
        string? PreviousMinLevel,
        string? MinLevel,
        bool? PreviousTeeLiveToFile,
        bool? TeeLiveToFile,
        bool? PreviousTeeLiveToUrl,
        bool? TeeLiveToUrl);

    /// <summary>
    /// Apply a per-sink runtime override (run state, tees, sink-level
    /// minimum). Updates the holders, the builder, and the on-disk
    /// JSON so a reboot restores the operator's choice. Single entry
    /// point — every PATCH on a sink's runtime knobs lands here.
    /// </summary>
    /// <param name="pipelineName">Pipeline this sink belongs to.</param>
    /// <param name="sinkId">Sink id (e.g. <c>text_file</c>).</param>
    /// <param name="incoming">
    /// The PATCH payload — every non-null field is applied; null
    /// fields keep the existing snapshot's value.
    /// </param>
    /// <returns>
    /// Before / after values for every field the call mutated, plus
    /// the wrapping <see cref="ManagementResult"/>. A null
    /// "after" field means the call did not touch that field.
    /// </returns>
    public SinkRuntimeApplyResult ApplySinkRuntime(
        string pipelineName,
        string sinkId,
        Configuration.Runtime.SinkRuntimeOverride incoming)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sinkId);
        ArgumentNullException.ThrowIfNull(incoming);

        var overridesHolder = Routing.Loopback.SinkOverridesRegistry.Get(pipelineName, sinkId);
        var runStateHolder = Routing.Loopback.SinkRunStateRegistry.Get(pipelineName, sinkId);
        if (overridesHolder is null || runStateHolder is null)
        {
            return new SinkRuntimeApplyResult(
                ManagementResult.Fail($"Unknown sink '{sinkId}' on pipeline '{pipelineName}'."),
                null, null, null, null, null, null, null, null);
        }

        // RunState — parse the string into the enum; reject anything
        // that isn't one of the three legal values up front so the
        // holder never sees garbage.
        Configuration.Runtime.SinkRunState? previousRunState = null;
        Configuration.Runtime.SinkRunState? nextRunState = null;
        if (incoming.RunState is not null)
        {
            var parsed = ParseRunStateOrNull(incoming.RunState);
            if (parsed is null)
            {
                return new SinkRuntimeApplyResult(
                    ManagementResult.Fail($"Unknown runState '{incoming.RunState}'. Expected 'disabled', 'live', or 'test'."),
                    null, null, null, null, null, null, null, null);
            }
            nextRunState = parsed.Value;
            previousRunState = runStateHolder.Set(parsed.Value);
        }

        // MinLevel — "none"/""/null clears the gate; any other value
        // must match a registered level.
        Levels.LogLevel? previousMinLevel = null;
        Levels.LogLevel? nextMinLevel = null;
        var minLevelChanged = false;
        if (incoming.MinLevel is not null)
        {
            minLevelChanged = true;
            var key = incoming.MinLevel.Trim();
            if (string.IsNullOrEmpty(key) || key.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                nextMinLevel = null;
            }
            else
            {
                nextMinLevel = _result.LevelRegistry.GetByKeyOrNull(key);
                if (nextMinLevel is null)
                {
                    return new SinkRuntimeApplyResult(
                        ManagementResult.Fail($"Unknown level '{key}'. Use a registered level key or 'none' to clear the gate."),
                        null, null, null, null, null, null, null, null);
                }
            }
            previousMinLevel = overridesHolder.SetMinLevel(nextMinLevel);
        }

        bool? previousTeeFile = null;
        if (incoming.TeeLiveToFile is { } teeFile)
            previousTeeFile = overridesHolder.SetTeeLiveToFile(teeFile);

        bool? previousTeeUrl = null;
        if (incoming.TeeLiveToUrl is { } teeUrl)
            previousTeeUrl = overridesHolder.SetTeeLiveToUrl(teeUrl);

        // Mirror to the builder so the JSON serializer carries the
        // change forward. Null fields fall through MergeWith. For
        // MinLevel an explicit clear ("none" / "") MUST overwrite a
        // previous gate, so we store the literal "none" sentinel
        // here rather than null — null in MergeWith means "don't
        // touch this field" and would silently keep the old value.
        var snapshotForBuilder = new Configuration.Runtime.SinkRuntimeOverride(
            RunState: nextRunState?.ToString().ToLowerInvariant(),
            TeeLiveToFile: incoming.TeeLiveToFile,
            TeeLiveToUrl: incoming.TeeLiveToUrl,
            MinLevel: minLevelChanged ? (nextMinLevel?.Key ?? "none") : null);
        _builder.SinkRuntimeOverrides.Merge(sinkId, snapshotForBuilder);

        // Surface persistence failure through the wrapped ManagementResult so
        // the dashboard tells the operator the runtime was applied IN MEMORY
        // but the on-disk config wasn't updated. Previously the swallowed
        // exception let the API report success on a vaporised edit.
        var persistError = PersistConfig();
        var apiResult = persistError is null
            ? ManagementResult.Ok($"Sink '{sinkId}' runtime updated.")
            : ManagementResult.Fail(
                $"Sink '{sinkId}' runtime updated in memory but {FormatPersistFailure(persistError)}");

        return new SinkRuntimeApplyResult(
            apiResult,
            PreviousRunState:      previousRunState?.ToString().ToLowerInvariant(),
            RunState:              nextRunState?.ToString().ToLowerInvariant(),
            PreviousMinLevel:      minLevelChanged ? (previousMinLevel?.Key ?? "none") : null,
            MinLevel:              minLevelChanged ? (nextMinLevel?.Key ?? "none") : null,
            PreviousTeeLiveToFile: previousTeeFile,
            TeeLiveToFile:         incoming.TeeLiveToFile,
            PreviousTeeLiveToUrl:  previousTeeUrl,
            TeeLiveToUrl:          incoming.TeeLiveToUrl);
    }

    private static Configuration.Runtime.SinkRunState? ParseRunStateOrNull(string raw) =>
        raw.Trim().ToLowerInvariant() switch
        {
            "disabled" => Configuration.Runtime.SinkRunState.Disabled,
            "live"     => Configuration.Runtime.SinkRunState.Live,
            "test"     => Configuration.Runtime.SinkRunState.Test,
            _          => (Configuration.Runtime.SinkRunState?)null
        };

    /// Applies everything to the builder, saves to disk, then attempts hot-swap.
    /// </summary>
    public ManagementResult CommitFull(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Apply strategy from steps
            if (root.TryGetProperty("steps", out var stepsEl) && stepsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var stepNames = new List<string>();
                foreach (var stepEl in stepsEl.EnumerateArray())
                {
                    if (stepEl.TryGetProperty("stepName", out var nameEl))
                    {
                        var stepName = nameEl.GetString();
                        if (!string.IsNullOrEmpty(stepName))
                            stepNames.Add(stepName);
                    }

                    // Apply alias if present
                    if (stepEl.TryGetProperty("alias", out var aliasEl) && aliasEl.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var stepName = stepEl.GetProperty("stepName").GetString();
                        var alias = aliasEl.GetString();
                        if (!string.IsNullOrEmpty(stepName))
                            _builder.WithAlias(stepName, alias ?? "");
                    }
                }

                if (stepNames.Count > 0)
                {
                    var strategy = Configuration.PipelineStrategy.FromNames(stepNames);
                    _builder.WithPipelineStrategy(strategy);
                }
            }

            // Apply sink configurations with full CRUD:
            // 1. Snapshot which sinks currently exist on the builder
            // 2. Ensure each sink in the payload exists (create if new)
            // 3. Apply config updates
            // 4. Remove sinks that are no longer in the payload
            if (root.TryGetProperty("sinks", out var sinksEl) && sinksEl.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var priorInspection = _builder.Inspect();

                // Collect the sink kinds requested in this commit
                var requestedKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var sinkEl in sinksEl.EnumerateArray())
                {
                    var rawId = sinkEl.TryGetProperty("sinkId", out var idEl) ? idEl.GetString() : null;
                    if (string.IsNullOrEmpty(rawId)) continue;
                    var sinkKind = rawId.Contains(':') ? rawId.Split(':')[0] : rawId;
                    requestedKinds.Add(sinkKind);
                }

                // Remove sinks that existed before but are not in the new payload
                if (priorInspection.HasConsoleSink && !requestedKinds.Contains(Services.KnownSinkKinds.Console))
                    _builder.WithoutConsoleSink();
                if (priorInspection.HasFileSink)
                {
                    var priorFileKind = priorInspection.FileKind ?? Services.KnownSinkKinds.TextFile;
                    if (!requestedKinds.Contains(Services.KnownSinkKinds.JsonFile) &&
                        !requestedKinds.Contains(Services.KnownSinkKinds.TextFile) &&
                        !requestedKinds.Contains(Services.KnownSinkKinds.ProtobufFile))
                        _builder.WithoutFileSink();
                }

                // Ensure sinks exist before applying config (create if new)
                if (requestedKinds.Contains(Services.KnownSinkKinds.Console) && !priorInspection.HasConsoleSink)
                    _builder.WithConsoleSink();
                // File sinks are created via WithFileSink() inside ApplySinkConfig, which handles both create and update

                foreach (var sinkEl in sinksEl.EnumerateArray())
                {
                    // Apply alias
                    if (sinkEl.TryGetProperty("sinkId", out var sinkIdEl) &&
                        sinkEl.TryGetProperty("alias", out var sinkAliasEl) &&
                        sinkAliasEl.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var sinkId = sinkIdEl.GetString();
                        var alias = sinkAliasEl.GetString();
                        if (!string.IsNullOrEmpty(sinkId))
                            _builder.WithAlias(sinkId, alias ?? "");
                    }

                    // Apply sink config properties
                    if (sinkEl.TryGetProperty("config", out var configEl) && configEl.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        ApplySinkConfig(sinkEl, configEl);
                    }
                }
            }

            // Apply pipeline-level loopback fields. Each is optional
            // and only writes when present in the body so a partial
            // commit (e.g. "edit only the sinks") leaves these
            // settings untouched.
            if (root.TryGetProperty("testLoopbackUrl", out var lbUrlEl))
            {
                _builder.WithTestLoopbackUrl(
                    lbUrlEl.ValueKind == System.Text.Json.JsonValueKind.String ? lbUrlEl.GetString() : null);
            }
            if (root.TryGetProperty("testLoopbackLogDir", out var lbDirEl))
            {
                _builder.WithTestLoopbackLogDir(
                    lbDirEl.ValueKind == System.Text.Json.JsonValueKind.String ? lbDirEl.GetString() : null);
            }
            if (root.TryGetProperty("loopbackEntriesPerFile", out var lbCapEl)
                && lbCapEl.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                _builder.WithLoopbackEntriesPerFile(lbCapEl.GetInt32());
            }
            if (root.TryGetProperty("loopbackUseNdjson", out var lbFmtEl)
                && (lbFmtEl.ValueKind == System.Text.Json.JsonValueKind.True
                 || lbFmtEl.ValueKind == System.Text.Json.JsonValueKind.False))
            {
                _builder.WithLoopbackUseNdjson(lbFmtEl.GetBoolean());
            }

            // Validate
            var validation = _builder.Validate();
            if (validation.HasCritical)
            {
                var messages = string.Join("; ", validation.Issues
                    .Where(i => i.Severity == ValidationSeverity.Critical)
                    .Select(i => i.Message));
                return ManagementResult.Fail($"Validation failed: {messages}");
            }

            // Save config to disk (always — essential operation). A
            // failure here is fatal to CommitFull because the whole
            // contract is "configuration saved AND pipeline updated";
            // returning Ok with the save quietly dropped is the lie
            // this fix exists to eliminate.
            var persistError = PersistConfig();
            if (persistError is not null)
                return ManagementResult.Fail(FormatPersistFailure(persistError));

            // Attempt hot-swap first (zero downtime)
            var swapped = _result.RebuildFrom(_builder);
            if (swapped)
                return ManagementResult.Ok("Configuration saved and pipeline rebuilt.");

            // Hot-swap unavailable — try rebuild with downtime (messages lost during rebuild)
            if (_registration is not null)
                return RebuildWithDowntime();

            return ManagementResult.Ok("Configuration saved. Pipeline unchanged (restart required).");
        }
        catch (Exception ex)
        {
            return ManagementResult.Fail($"Commit failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply sink-specific configuration from the commit payload.
    /// Maps JSON properties back to builder methods.
    /// </summary>
    private void ApplySinkConfig(System.Text.Json.JsonElement sinkEl, System.Text.Json.JsonElement configEl)
    {
        var sinkId = sinkEl.TryGetProperty("sinkId", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrEmpty(sinkId)) return;

        // Determine sink kind from sinkId (e.g. "console", "json_file", "json_file:new_1")
        var kind = sinkId.Contains(':') ? sinkId.Split(':')[0] : sinkId;

        // v2 contract: when the dashboard sends `config: { properties: {…} }`,
        // the inner bag is the canonical payload. Lift it so the rest of
        // this method reads from the bag without per-sink branches.
        // Sinks still on the v1 contract send a flat config and the
        // original element flows through unchanged.
        var sinkConfigEl = configEl;
        var isV2Envelope = false;
        if (configEl.TryGetProperty("properties", out var propsEl)
            && propsEl.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            sinkConfigEl = propsEl;
            isV2Envelope = true;
        }

        // Apply minLevel if present. minLevel is metadata, not a sink-config
        // property, so it stays on the outer envelope under both contracts.
        if (configEl.TryGetProperty("minLevel", out var minLevelEl))
        {
            var minLevel = minLevelEl.ValueKind == System.Text.Json.JsonValueKind.String ? minLevelEl.GetString() : null;
            if (kind == Services.KnownSinkKinds.Console)
                _builder.UpdateConsoleMinLevel(minLevel);
            else if (kind == Services.KnownSinkKinds.JsonFile || kind == Services.KnownSinkKinds.TextFile)
                _builder.UpdateFileMinLevel(minLevel);
        }

        // Apply file sink properties. The dashboard POSTs camelCase JSON
        // matching FileSinkConfig's [JsonPropertyName] attributes, so a
        // single Deserialize call replaces the per-key TryGetProperty
        // pile that used to live here.
        //
        // Both contract shapes deserialize into the same FileSinkConfig
        // because the bag's keys (logDirectory, fileNamePattern, etc.)
        // mirror the v1 flat-key names — the v2 lift above just sees
        // through the `properties` envelope. The fileConfig.MinLevel
        // path picks up minLevel from the OUTER envelope through a
        // dedicated read so v2 sinks don't lose level metadata.
        if (kind == Services.KnownSinkKinds.JsonFile || kind == Services.KnownSinkKinds.TextFile || kind == Services.KnownSinkKinds.ProtobufFile)
        {
            // Source-gen typed overload keeps this AOT/trim-safe — see
            // HeraldJsonContext for the FileSinkConfig binding.
            var fileConfig = sinkConfigEl.Deserialize(Configuration.HeraldJsonContext.Default.FileSinkConfig)
                ?? new FileSinkConfig();
            // text_file's v2 mmpform renamed fileNamePattern → namePattern.
            // FileSinkConfig still binds the legacy name; pull the alt
            // spelling out of the bag so a v2 commit's name pattern lands.
            if (string.IsNullOrEmpty(fileConfig.FileNamePattern)
                && sinkConfigEl.TryGetProperty("namePattern", out var npEl)
                && npEl.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                fileConfig = fileConfig with { FileNamePattern = npEl.GetString() };
            }
            // minLevel rides on the outer envelope, not in the bag — re-read
            // it here when the v2 lift moved focus to the inner properties.
            if (isV2Envelope
                && configEl.TryGetProperty("minLevel", out var outerMin)
                && outerMin.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                fileConfig = fileConfig with { MinLevel = outerMin.GetString() };
            }
            if (!string.IsNullOrEmpty(fileConfig.LogFileTemplate))
            {
                var path = string.IsNullOrEmpty(fileConfig.LogDirectory)
                    ? fileConfig.LogFileTemplate
                    : $"{fileConfig.LogDirectory}/{fileConfig.LogFileTemplate}";
                if (!string.IsNullOrEmpty(fileConfig.LogExtension)) path += $".{fileConfig.LogExtension}";

                if (fileConfig.RollingLogsEnabled)
                {
                    // fileNamePattern is the .NET date format injected into rolled
                    // file names. It maps to JsonFileRollingConfig.FileNameSuffix
                    // — the same field BuilderInspection surfaces back as
                    // FileNamePattern, so the form's value round-trips through
                    // both directions of the API.
                    _builder.WithFileSink(
                        path,
                        fileConfig.RollingInterval ?? "daily",
                        maxBytes:          ParseFileSize(fileConfig.MaxFileSize),
                        maxRetainedFiles:  fileConfig.MaxRetainedFiles,
                        fileNameSuffix:    fileConfig.FileNamePattern,
                        minLevel:          fileConfig.MinLevel,
                        retentionDays:     fileConfig.RetentionDays,
                        totalSizeCapBytes: ParseFileSize(fileConfig.TotalSizeCap));
                }
                else
                {
                    _builder.WithFileSink(path, minLevel: fileConfig.MinLevel);
                }
            }
        }

        // Network / integration sinks. The per-kind dispatch lives in
        // NetworkSinkDispatch so this method and RestoreBuilderFromConfig
        // share one table — adding a new URI sink takes one row, not
        // two parallel branches.
        if (NetworkSinkDispatch.UriSinks.TryGetValue(kind, out var uriApply))
        {
            var uri = ReadStringProperty(configEl, "uri");
            var minLvl = ReadStringProperty(configEl, "minLevel");
            // Optional headers ride on the same JSON shape the round-trip
            // emits: { "headers": { "Authorization": "Bearer ..." } }.
            // Missing object → null; missing keys / non-string values are
            // skipped silently so a malformed header entry does not crash
            // sink wiring.
            var headers = ReadHeadersProperty(configEl);
            if (!string.IsNullOrEmpty(uri)) uriApply(_builder, uri, minLvl, headers);
        }
        else if (NetworkSinkDispatch.HostPortSinks.TryGetValue(kind, out var hpSpec))
        {
            var host = ReadStringProperty(configEl, "host");
            var port = ReadIntProperty(configEl, "port") ?? hpSpec.DefaultPort;
            var minLvl = ReadStringProperty(configEl, "minLevel");
            if (!string.IsNullOrEmpty(host)) hpSpec.Apply(_builder, host, port, minLvl);
        }
        else if (kind == Services.KnownSinkKinds.Channel)
        {
            var channelName = configEl.TryGetProperty("name", out var cnEl) && cnEl.ValueKind == System.Text.Json.JsonValueKind.String ? cnEl.GetString() : null;
            if (!string.IsNullOrEmpty(channelName))
            {
                // Channel sinks need a writer — use the default console writer as a placeholder
                // The real writer is configured at construction time, not through JSON
            }
        }

        // Apply config to custom configurable sink providers
        var provider = _builder.SinkProviders.Get(kind);
        if (provider is Routing.IConfigurableSinkProvider configurable)
        {
            var values = new Dictionary<string, object?>();
            foreach (var prop in configEl.EnumerateObject())
            {
                values[prop.Name] = prop.Value.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => prop.Value.GetString(),
                    System.Text.Json.JsonValueKind.Number => prop.Value.TryGetInt32(out var i) ? i : prop.Value.GetDouble(),
                    System.Text.Json.JsonValueKind.True => true,
                    System.Text.Json.JsonValueKind.False => false,
                    _ => null
                };
            }
            configurable.ApplyConfiguration(values);
        }
    }

    /// <summary>Parse human-readable file size (100MB, 1GB, 500KB) to bytes.</summary>
    private static long? ParseFileSize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "0") return null;
        value = value.Trim().ToUpperInvariant();
        if (value.EndsWith("GB")) return (long)(double.Parse(value[..^2]) * 1024 * 1024 * 1024);
        if (value.EndsWith("MB")) return (long)(double.Parse(value[..^2]) * 1024 * 1024);
        if (value.EndsWith("KB")) return (long)(double.Parse(value[..^2]) * 1024);
        if (long.TryParse(value, out var bytes)) return bytes;
        return null;
    }

    // Small JSON-element readers shared by the network-sink dispatch
    // (and anywhere else that needs the "string | null" / "int | null"
    // shape from a TryGetProperty + ValueKind check). One pair instead
    // of inline two-liners scattered across the file.
    private static string? ReadStringProperty(System.Text.Json.JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
            ? v.GetString()
            : null;

    private static int? ReadIntProperty(System.Text.Json.JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number
            ? v.GetInt32()
            : (int?)null;

    /// <summary>
    /// Read an optional <c>headers</c> object out of a sink-config JSON
    /// element. Used by ApplySinkConfig so the dashboard's "save sink"
    /// payload can carry Bearer-token / API-key headers alongside the
    /// usual uri + minLevel fields. Non-string values are skipped; an
    /// absent or empty object yields null so sinks that don't use
    /// headers see the pre-existing shape exactly.
    ///
    /// <para>Names and values are validated through
    /// <see cref="HttpHeaderSanitizer.Sanitize"/> so a CRLF in a value
    /// or a non-token character in a name silently drops the entry
    /// rather than letting it ride through to the HTTP client and
    /// split the request on the wire.</para>
    /// </summary>
    private static IReadOnlyDictionary<string, string>? ReadHeadersProperty(System.Text.Json.JsonElement el)
    {
        if (!el.TryGetProperty("headers", out var v)) return null;
        if (v.ValueKind != System.Text.Json.JsonValueKind.Object) return null;

        var result = new Dictionary<string, string>(System.StringComparer.Ordinal);
        foreach (var prop in v.EnumerateObject())
        {
            if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var s = prop.Value.GetString();
                if (s is not null) result[prop.Name] = s;
            }
        }
        return HttpHeaderSanitizer.Sanitize(result);
    }

    // ── Rebuild With Downtime ────────────────────────────────────────

    /// <summary>
    /// Rebuild a pipeline that doesn't have hot-reload (e.g. HotPath entry point).
    /// Messages are lost during the rebuild window.
    ///
    /// 1. The registration's Result is replaced with a new build
    /// 2. The old pipeline continues processing in-flight events
    /// 3. New callers get the new pipeline via HeraldRegistry.Get()
    /// 4. Old callers who cached the Logger reference still use the old one
    ///
    /// For the Dashboard, this is a "soft reset" — the config is saved,
    /// the pipeline is rebuilt, and the registration is updated.
    /// </summary>
    public ManagementResult RebuildWithDowntime()
    {
        if (_registration is null)
            return ManagementResult.Fail("RebuildWithDowntime requires a registered pipeline.");

        var validation = _builder.Validate();
        if (validation.HasCritical)
        {
            var messages = string.Join("; ", validation.Issues
                .Where(i => i.Severity == ValidationSeverity.Critical)
                .Select(i => i.Message));
            return ManagementResult.Fail($"Validation failed: {messages}");
        }

        // Save config first. Fail before tearing down the live
        // pipeline if the disk write didn't succeed — a downtime
        // rebuild that loses both the running pipeline AND the
        // on-disk record is the worst possible outcome.
        var persistError = PersistConfig();
        if (persistError is not null)
            return ManagementResult.Fail(FormatPersistFailure(persistError));

        // Build a completely new pipeline
        var newResult = _builder.BuildAndCommit();

        // Swap the registration's result — new callers get the new pipeline
        var oldResult = _registration.Result;
        _registration.Result = newResult;

        // Update our own reference so subsequent API calls use the new result
        _result = newResult;

        // Invalidate cached API on registration so it rebuilds with new result
        _registration.InvalidateApi();

        return ManagementResult.Ok(
            "Pipeline rebuilt with downtime. Configuration saved. " +
            "Events during rebuild were dropped. New pipeline is active.");
    }

    // ── Transaction Operations ───────────────────────────────────────

    /// <summary>
    /// Begin a transaction. Snapshots the current builder state as JSON.
    /// All subsequent changes are staged until Commit() or Rollback().
    /// </summary>
    public ManagementResult BeginTransaction()
    {
        if (_inTransaction)
            return ManagementResult.Fail("Transaction already active. Commit or rollback first.");

        _snapshot = _builder.ExportConfig();
        _inTransaction = true;
        return ManagementResult.Ok("Transaction started. Make changes, then commit or rollback.");
    }

    /// <summary>
    /// Commit the transaction: rebuild the pipeline from the current builder state
    /// and swap atomically via SwappableLogger.
    /// </summary>
    public ManagementResult CommitTransaction()
    {
        if (!_inTransaction)
            return ManagementResult.Fail("No active transaction. Call BeginTransaction first.");

        var validation = _builder.Validate();
        if (validation.HasCritical)
        {
            var messages = string.Join("; ", validation.Issues
                .Where(i => i.Severity == ValidationSeverity.Critical)
                .Select(i => i.Message));
            return ManagementResult.Fail($"Validation failed: {messages}");
        }

        _inTransaction = false;
        _snapshot = null;

        // Always save config to disk — this is the essential operation.
        // The config represents the desired state regardless of whether
        // the live pipeline can be hot-swapped right now. Fail loudly
        // when the write fails: a commit that reports Ok but never
        // touched disk is the exact lie this fix removes.
        var persistError = PersistConfig();
        if (persistError is not null)
            return ManagementResult.Fail(FormatPersistFailure(persistError));

        // Attempt to hot-swap the live pipeline. Non-fatal if it fails —
        // the config is saved and will be applied on next restart.
        var swapped = _result.RebuildFrom(_builder);
        if (swapped)
            return ManagementResult.Ok("Configuration saved. Pipeline rebuilt and swapped atomically.");

        // Hot-swap unavailable — try rebuild with downtime
        if (_registration is not null)
            return RebuildWithDowntime();

        return ManagementResult.Ok("Configuration saved. Pipeline unchanged (restart required).");
    }

    /// <summary>
    /// Rollback the transaction: restore the builder to the snapshot taken at Begin().
    /// No pipeline change occurs.
    /// </summary>
    public ManagementResult RollbackTransaction()
    {
        if (!_inTransaction || _snapshot is null)
            return ManagementResult.Fail("No active transaction to rollback.");

        // Restore builder state by resetting and re-applying from snapshot
        // Note: We can't fully deserialize back into the builder (some types aren't
        // JSON-representable like ILogSinkProvider instances), but we can restore
        // the scalar config by rebuilding from the snapshot JSON.
        _builder.Reset();
        RestoreFromJson(_snapshot);

        _inTransaction = false;
        _snapshot = null;
        return ManagementResult.Ok("Transaction rolled back. Builder restored to pre-transaction state.");
    }

    /// <summary>Whether a transaction is currently active.</summary>
    public bool IsTransactionActive => _inTransaction;

    // ── Sample Data ───────────────────────────────────────────────────

    /// <summary>
    /// Get sample log data as NDJSON. Use instead of live data for dashboard
    /// development, demos, and integration testing.
    /// </summary>
    /// <param name="count">Number of entries (default: 100).</param>
    /// <param name="file">Which sample file: 1 = day 1 (April 10), 2 = day 2 (April 11). Default: 1.</param>
    public string GetSampleData(int count = 100, int file = 1)
    {
        var baseTime = file == 2
            ? new DateTimeOffset(2026, 4, 11, 0, 0, 0, TimeSpan.Zero)
            : new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero);
        return SampleDataGenerator.GenerateNdjson(count, baseTime);
    }

    /// <summary>
    /// Get sample log data as a list of parsed JSON objects.
    /// Each line is a complete Herald NDJSON record with realistic game server data.
    /// </summary>
    public IReadOnlyList<string> GetSampleDataLines(int count = 100, int file = 1)
    {
        var baseTime = file == 2
            ? new DateTimeOffset(2026, 4, 11, 0, 0, 0, TimeSpan.Zero)
            : new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero);
        return SampleDataGenerator.Generate(count, baseTime);
    }

    /// <summary>
    /// Write sample rolled log files to the specified directory.
    /// Creates two files simulating a daily roll-over, each with 100 entries.
    /// Returns the paths of the two files created.
    /// </summary>
    public (string File1, string File2) WriteSampleFiles(string directory)
    {
        return SampleDataGenerator.WriteSampleRolledFiles(directory);
    }

    // ── Write Operations: Scalars ────────────────────────────────────

    public ManagementResult SetMinimumLevel(string level)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(level);
        _builder.WithMinimumLevel(level);
        return AutoCommitOrStage($"Minimum level set to '{level}'.");
    }

    public ManagementResult SetConsoleSink(bool enabled, string? minLevel = null)
    {
        if (enabled)
            _builder.WithConsoleSink(minLevel: minLevel);
        else
            _builder.WithoutConsoleSink();
        return AutoCommitOrStage(enabled ? "Console sink enabled." : "Console sink removed.");
    }

    public ManagementResult UpdateConsoleMinLevel(string? minLevel)
    {
        try
        {
            _builder.UpdateConsoleMinLevel(minLevel);
            return AutoCommitOrStage($"Console minimum level updated to '{minLevel ?? "inherit"}'.");
        }
        catch (InvalidOperationException ex)
        {
            return ManagementResult.Fail(ex.Message);
        }
    }

    public ManagementResult SetFileSink(bool enabled, string? path = null, string? minLevel = null)
    {
        if (enabled)
        {
            if (string.IsNullOrWhiteSpace(path))
                return ManagementResult.Fail("File path is required when enabling file sink.");
            _builder.WithFileSink(path, minLevel: minLevel);
        }
        else
        {
            _builder.WithoutFileSink();
        }
        return AutoCommitOrStage(enabled ? $"File sink enabled at '{path}'." : "File sink removed.");
    }

    public ManagementResult UpdateFileMinLevel(string? minLevel)
    {
        try
        {
            _builder.UpdateFileMinLevel(minLevel);
            return AutoCommitOrStage($"File minimum level updated to '{minLevel ?? "inherit"}'.");
        }
        catch (InvalidOperationException ex)
        {
            return ManagementResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Update the file sink's retention policy (retention days and/or total size cap).
    /// File sink must already be configured.
    /// </summary>
    /// <summary>
    /// Update the file sink's retention policy (retention days and/or total size cap).
    /// File sink must already be configured.
    /// </summary>
    public ManagementResult UpdateFileRetentionPolicy(int? retentionDays = null, long? totalSizeCapBytes = null)
    {
        try
        {
            var inspection = _builder.Inspect();
            if (!inspection.HasFileSink)
                return ManagementResult.Fail("No file sink configured.");

            // Preserve existing rolling settings and merge in retention
            var policy = new Configuration.FileSinkPolicy(
                Interval: inspection.FileRollingInterval ?? Services.JsonConfigProperties.IntervalNone,
                MaxBytes: inspection.FileMaxBytes,
                MaxRetainedFiles: inspection.FileMaxRetainedFiles,
                FileNameSuffix: inspection.FileNamePattern,
                RetentionDays: retentionDays,
                TotalSizeCapBytes: totalSizeCapBytes);

            _builder.UpdateFileRollingPolicy(policy);
            var parts = new List<string>();
            if (retentionDays.HasValue) parts.Add($"retentionDays={retentionDays}");
            if (totalSizeCapBytes.HasValue) parts.Add($"totalSizeCap={totalSizeCapBytes}");
            return AutoCommitOrStage($"File retention policy updated: {string.Join(", ", parts)}.");
        }
        catch (InvalidOperationException ex)
        {
            return ManagementResult.Fail(ex.Message);
        }
    }

    public ManagementResult SetPipelineStrategy(string strategyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyName);

        PipelineStrategy strategy = strategyName.ToLowerInvariant() switch
        {
            "default" => PipelineStrategy.Default(),
            "filterearly" or "filter_early" or "filter-early" => PipelineStrategy.FilterEarly(),
            "minimal" => PipelineStrategy.Minimal(),
            _ => throw new ArgumentException($"Unknown strategy: '{strategyName}'. Use: default, filterEarly, minimal.")
        };

        _builder.WithPipelineStrategy(strategy);
        return AutoCommitOrStage($"Pipeline strategy set to '{strategyName}'.");
    }

    public ManagementResult SetPipelineStrategyCustom(IReadOnlyList<string> stepNames)
    {
        ArgumentNullException.ThrowIfNull(stepNames);
        try
        {
            var strategy = PipelineStrategy.FromNames(stepNames);
            _builder.WithPipelineStrategy(strategy);
            return AutoCommitOrStage($"Custom pipeline strategy set: [{string.Join(", ", stepNames)}].");
        }
        catch (ArgumentException ex)
        {
            return ManagementResult.Fail(ex.Message);
        }
    }

    public ManagementResult SetTraceCorrelation(bool enabled)
    {
        if (enabled)
            _builder.WithTraceCorrelation();
        else
            _builder.WithoutTraceCorrelation();
        return AutoCommitOrStage(enabled ? "Trace correlation enabled." : "Trace correlation disabled.");
    }

    public ManagementResult SetLevelDump(bool enabled)
    {
        if (enabled)
            _builder.WithLevelDump();
        else
            _builder.WithoutLevelDump();
        return AutoCommitOrStage(enabled ? "Level dump enabled." : "Level dump disabled.");
    }

    // ── Write Operations: Custom Levels ────────────────────────────

    /// <summary>Get all custom (non-base) log levels.</summary>
    public IReadOnlyList<Levels.LogLevel> GetCustomLevels() => _builder.GetCustomLevels();

    /// <summary>Add or update a custom log level.</summary>
    public ManagementResult AddCustomLevel(string key, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        _builder.WithCustomLevel(key, displayName);
        return AutoCommitOrStage($"Custom level '{key}' ({displayName}) added.");
    }

    /// <summary>Remove a custom log level by key. Base levels cannot be removed.</summary>
    public ManagementResult RemoveCustomLevel(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _builder.WithoutCustomLevel(key);
        return AutoCommitOrStage($"Custom level '{key}' removed.");
    }

    /// <summary>Remove all custom log levels.</summary>
    public ManagementResult ClearCustomLevels()
    {
        _builder.ClearCustomLevels();
        return AutoCommitOrStage("All custom log levels cleared.");
    }

    /// <summary>
    /// Reorder the level registry by key. The supplied array becomes
    /// the new <c>baseLevels</c> ordering, which the runtime uses to
    /// derive ranks. Built-in keys missing from the request are
    /// appended in canonical order; unknown keys are skipped.
    ///
    /// Pass null or an empty array to revert to the canonical order.
    ///
    /// The dashboard's Categories tab calls this through
    /// <c>PUT /api/registry/{name}/levels/order</c>. After the order
    /// is recorded on the builder, AutoCommit triggers a hot reload
    /// so <see cref="LevelFilter"/> sees the new ranks immediately.
    /// </summary>
    public ManagementResult SetLevelOrder(IEnumerable<string>? keys)
    {
        _builder.WithLevelOrder(keys);
        var order = _builder.GetLevelOrder();
        var summary = order is null || order.Count == 0
            ? "Level order reset to canonical default."
            : $"Level order set to [{string.Join(", ", order)}].";
        return AutoCommitOrStage(summary);
    }

    // ── Write Operations: Level Styles ─────────────────────────────

    /// <summary>
    /// Get the current level styles (merged defaults + overrides).
    /// Returns the effective style for each level: key, color, bold, italic, background.
    /// </summary>
    public IReadOnlyList<Quick.LevelStyleInfo> GetLevelStyles()
    {
        var inspection = _builder.Inspect();
        return inspection.LevelStyles ?? [];
    }

    /// <summary>
    /// Set or override the display style for a log level.
    /// </summary>
    public ManagementResult SetLevelStyle(string levelKey, string colorName,
        bool bold = false, bool italic = false, string? backgroundColorName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(levelKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(colorName);
        _builder.WithLevelStyle(levelKey, colorName, bold, italic, backgroundColorName);
        return AutoCommitOrStage($"Level style for '{levelKey}' set to {colorName}{(bold ? " bold" : "")}{(italic ? " italic" : "")}.");
    }

    /// <summary>Remove a level style override (revert to default for that level).</summary>
    public ManagementResult RemoveLevelStyle(string levelKey)
    {
        _builder.WithoutLevelStyle(levelKey);
        return AutoCommitOrStage($"Level style override for '{levelKey}' removed.");
    }

    /// <summary>Clear all level style overrides (revert to defaults).</summary>
    public ManagementResult ClearLevelStyles()
    {
        _builder.ClearLevelStyles();
        return AutoCommitOrStage("All level style overrides cleared. Defaults restored.");
    }

    // ── Write Operations: Pipeline Policy (Async / Batching / Sampling) ──
    //
    // Three focused setters the dashboard's per-step config panels call
    // through PUT /api/registry/{name}/pipeline/{async|batching|sampling}.
    // Each one validates input at the boundary, mutates the builder via
    // the existing With*/Without* fluent methods, and goes through
    // AutoCommitOrStage so hot reload picks up the change. Bad input
    // returns ManagementResult.Fail without touching the builder.

    /// <summary>
    /// Configure the async-logging step. Pass <paramref name="enabled"/>=false
    /// to switch the pipeline back to synchronous; the other parameters are
    /// ignored in that case. When enabled, a positive capacity and a known
    /// drop strategy (see <see cref="Services.KnownDropStrategies"/>) are
    /// required.
    /// </summary>
    public ManagementResult SetAsyncConfig(
        bool enabled,
        int? capacity = null,
        string? dropStrategy = null,
        bool? deferRendering = null)
    {
        if (!enabled)
        {
            _builder.WithoutAsyncLogging();
            return AutoCommitOrStage("Async logging disabled.");
        }

        // Guard clauses: validate before any mutation so a rejected
        // request leaves the builder untouched.
        var capacityValue = capacity ?? Services.PipelineDefaults.AsyncCapacity;
        if (capacityValue <= 0)
            return ManagementResult.Fail($"Async capacity must be > 0 (got {capacityValue}).");

        var strategyValue = dropStrategy ?? Services.KnownDropStrategies.DropWrite;
        if (!IsKnownDropStrategy(strategyValue))
            return ManagementResult.Fail(
                $"Unknown async drop strategy '{strategyValue}'. Expected one of: " +
                $"{Services.KnownDropStrategies.DropWrite}, {Services.KnownDropStrategies.DropOldest}, {Services.KnownDropStrategies.Wait}.");

        var deferValue = deferRendering ?? false;
        _builder.WithAsyncLogging(capacityValue, strategyValue, deferValue);
        return AutoCommitOrStage(
            $"Async logging enabled (capacity={capacityValue}, dropStrategy={strategyValue}, deferRendering={deferValue}).");
    }

    /// <summary>
    /// Configure the batching step. Pass <paramref name="enabled"/>=false
    /// to disable batching; the size/delay parameters are ignored in that
    /// case. When enabled, both size and delay must be positive.
    /// </summary>
    public ManagementResult SetBatchingConfig(
        bool enabled,
        int? maxBatchSize = null,
        int? maxBatchDelayMs = null)
    {
        if (!enabled)
        {
            _builder.WithoutBatching();
            return AutoCommitOrStage("Batching disabled.");
        }

        var sizeValue = maxBatchSize ?? Services.PipelineDefaults.BatchSize;
        if (sizeValue <= 0)
            return ManagementResult.Fail($"Batch size must be > 0 (got {sizeValue}).");

        var delayValue = maxBatchDelayMs ?? Services.PipelineDefaults.BatchDelayMs;
        if (delayValue <= 0)
            return ManagementResult.Fail($"Batch delay must be > 0 ms (got {delayValue}).");

        _builder.WithBatching(sizeValue, delayValue);
        return AutoCommitOrStage(
            $"Batching enabled (maxBatchSize={sizeValue}, maxBatchDelayMs={delayValue}).");
    }

    /// <summary>
    /// Set the random sampling rate (1-in-N). A rate of 0 disables
    /// sampling entirely. Negative rates are rejected.
    /// </summary>
    public ManagementResult SetSamplingRate(int rate)
    {
        if (rate < 0)
            return ManagementResult.Fail($"Sampling rate must be >= 0 (got {rate}).");

        if (rate == 0)
        {
            _builder.WithoutSampling();
            return AutoCommitOrStage("Sampling disabled.");
        }

        _builder.WithSampling(rate);
        return AutoCommitOrStage($"Sampling enabled (1-in-{rate}).");
    }

    /// <summary>
    /// Configure the flight-recorder ring buffer. Pass <paramref name="enabled"/>=false
    /// to disable the recorder; the other parameters are ignored in that case.
    /// When enabled, <paramref name="bufferSize"/> must be positive. Pass
    /// <c>null</c> for either level to inherit the conventional default
    /// (pipeline minimum / "error").
    /// </summary>
    public ManagementResult SetFlightRecorderConfig(
        bool enabled,
        int? bufferSize = null,
        string? minLevel = null,
        string? triggerLevel = null)
    {
        if (!enabled)
        {
            _builder.WithoutFlightRecorder();
            return AutoCommitOrStage("Flight recorder disabled.");
        }

        var sizeValue = bufferSize ?? 200;
        if (sizeValue <= 0)
            return ManagementResult.Fail($"Flight-recorder buffer size must be > 0 (got {sizeValue}).");

        _builder.WithFlightRecorder(sizeValue, minLevel, triggerLevel);
        return AutoCommitOrStage(
            $"Flight recorder enabled (bufferSize={sizeValue}, minLevel={minLevel ?? "inherit"}, triggerLevel={triggerLevel ?? "error"}).");
    }

    /// <summary>
    /// Configure the post-filtering batch step. Pass <paramref name="enabled"/>=false
    /// to disable; predicates are ignored in that case. When enabled, all
    /// three predicates are required. Both batch sizing parameters fall back
    /// to <see cref="Services.PipelineDefaults"/> when zero or negative.
    /// </summary>
    public ManagementResult SetPostFilteringConfig(
        bool enabled,
        Predicates.PredicateSpec? triggerCondition = null,
        Predicates.PredicateSpec? normalFilter = null,
        Predicates.PredicateSpec? escalatedFilter = null,
        int? maxBatchSize = null,
        int? maxBatchDelayMs = null)
    {
        if (!enabled)
        {
            _builder.WithoutPostFiltering();
            return AutoCommitOrStage("Post-filtering disabled.");
        }

        if (triggerCondition is null || normalFilter is null || escalatedFilter is null)
            return ManagementResult.Fail("Post-filtering requires triggerCondition, normalFilter, and escalatedFilter.");

        var sizeValue = maxBatchSize ?? Services.PipelineDefaults.BatchSize;
        if (sizeValue <= 0)
            return ManagementResult.Fail($"Post-filtering batch size must be > 0 (got {sizeValue}).");

        var delayValue = maxBatchDelayMs ?? Services.PipelineDefaults.BatchDelayMs;
        if (delayValue <= 0)
            return ManagementResult.Fail($"Post-filtering batch delay must be > 0 ms (got {delayValue}).");

        _builder.WithPostFiltering(triggerCondition, normalFilter, escalatedFilter, sizeValue, delayValue);
        return AutoCommitOrStage(
            $"Post-filtering enabled (maxBatchSize={sizeValue}, maxBatchDelayMs={delayValue}).");
    }

    // Drop-strategy validation kept as a small private helper so the
    // valid set lives in one place. The known strategies are constants on
    // KnownDropStrategies, so a switch expression stays exhaustive and
    // dirt-cheap.
    private static bool IsKnownDropStrategy(string value) => value switch
    {
        Services.KnownDropStrategies.DropWrite => true,
        Services.KnownDropStrategies.DropOldest => true,
        Services.KnownDropStrategies.Wait => true,
        _ => false
    };

    // ── Write Operations: Enrichers ──────────────────────────────────

    public ManagementResult RemoveEnricher(string name)
    {
        _builder.Enrichers.Remove(name);
        return AutoCommitOrStage($"Enricher '{name}' removed.");
    }

    public ManagementResult ClearEnrichers()
    {
        _builder.Enrichers.Clear();
        return AutoCommitOrStage("All enrichers cleared.");
    }

    public ManagementResult ResetEnrichers()
    {
        _builder.Enrichers.Reset();
        return AutoCommitOrStage("Enrichers reset to defaults.");
    }

    // ── Write Operations: Event Processors ───────────────────────────

    public ManagementResult RemoveEventProcessor(string name)
    {
        _builder.WithoutEventProcessor(name);
        return AutoCommitOrStage($"Event processor '{name}' removed.");
    }

    public ManagementResult ClearEventProcessors()
    {
        _builder.ClearEventProcessors();
        return AutoCommitOrStage("All non-protected event processors cleared.");
    }

    // ── Write Operations: Property Styles ────────────────────────────

    public ManagementResult SetPropertyStyle(string propertyName, string colorName,
        bool bold = false, bool italic = false, string? backgroundColor = null)
    {
        _builder.SetPropertyStyle(propertyName, colorName, bold, italic, backgroundColor);
        return AutoCommitOrStage($"Property style set for '{propertyName}'.");
    }

    public ManagementResult RemovePropertyStyle(string propertyName)
    {
        _builder.WithoutPropertyStyle(propertyName);
        return AutoCommitOrStage($"Property style removed for '{propertyName}'.");
    }

    public ManagementResult ClearPropertyStyles()
    {
        _builder.ClearPropertyStyles();
        return AutoCommitOrStage("All custom property styles cleared.");
    }

    // -- Write Operations: Category (Channel) Styles ------------------

    /// <summary>
    /// Get the current category (channel) styles from the builder snapshot.
    /// Returns per-category colour / bold / italic / background; the list is
    /// empty when no category styles are configured. The presentation-side
    /// term is "channel"; the wire term is "category" so it matches the
    /// <c>LogEvent.Category</c> dimension.
    /// </summary>
    public IReadOnlyList<Quick.CategoryStyleInfo> GetCategoryStyles()
    {
        var inspection = _builder.Inspect();
        return inspection.CategoryStyles ?? [];
    }

    /// <summary>Set or replace the display style for a log category.</summary>
    public ManagementResult SetCategoryStyle(string categoryName, string? colorName = null,
        bool bold = false, bool italic = false, string? backgroundColorName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);
        _builder.SetCategoryStyle(categoryName, colorName, bold, italic, backgroundColorName);
        return AutoCommitOrStage($"Category style for '{categoryName}' set.");
    }

    /// <summary>Remove a category style override.</summary>
    public ManagementResult RemoveCategoryStyle(string categoryName)
    {
        _builder.WithoutCategoryStyle(categoryName);
        return AutoCommitOrStage($"Category style override for '{categoryName}' removed.");
    }

    /// <summary>Clear all category style overrides.</summary>
    public ManagementResult ClearCategoryStyles()
    {
        _builder.ClearCategoryStyles();
        return AutoCommitOrStage("All category style overrides cleared.");
    }

    // ── Write Operations: Aliases ─────────────────────────────────────

    /// <summary>Get all aliases.</summary>
    public IReadOnlyDictionary<string, string> GetAliases() => _builder.GetAliases();

    /// <summary>Set an alias for a pipeline step, sink, or filter.</summary>
    public ManagementResult SetAlias(string id, string alias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        _builder.WithAlias(id, alias ?? "");
        return AutoCommitOrStage($"Alias for '{id}' set to '{alias}'.");
    }

    /// <summary>Remove an alias.</summary>
    public ManagementResult RemoveAlias(string id)
    {
        _builder.WithoutAlias(id);
        return AutoCommitOrStage($"Alias for '{id}' removed.");
    }

    // ── Write Operations: Channels ───────────────────────────────────

    /// <summary>Get all configured channels.</summary>
    public IReadOnlyList<ChannelInfo> GetChannels()
    {
        var channels = new List<ChannelInfo>();
        var inspection = _builder.Inspect();
        foreach (var name in inspection.ChannelNames)
        {
            // We can't inspect the internal writer type, so report what we know
            channels.Add(new ChannelInfo(name, "configured", null, null));
        }
        return channels;
    }

    /// <summary>Add a new channel by kind.</summary>
    public ManagementResult AddChannel(ChannelInfo channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (string.IsNullOrWhiteSpace(channel.Name))
            return new ManagementResult(false, "Channel name is required.");

        MMP.Herald.Output.Rich.IRenderedLogOutputWriter writer = channel.OutputKind?.ToLowerInvariant() switch
        {
            "file" when !string.IsNullOrWhiteSpace(channel.Path) =>
                new MMP.Herald.Output.Writers.FileOutputWriter(channel.Path),
            "console" or null or "" =>
                new MMP.Herald.Output.Rich.DefaultRichConsoleWriter(),
            _ => new MMP.Herald.Output.Rich.DefaultRichConsoleWriter()
        };

        _builder.Channels.Add(channel.Name, writer);
        return AutoCommitOrStage($"Channel '{channel.Name}' added ({channel.OutputKind ?? "console"}).");
    }

    public ManagementResult RemoveChannel(string channelName)
    {
        _builder.WithoutChannel(channelName);
        return AutoCommitOrStage($"Channel '{channelName}' removed.");
    }

    public ManagementResult ClearChannels()
    {
        _builder.ClearChannels();
        return AutoCommitOrStage("All channels cleared.");
    }

    // ── Write Operations: Bulk ───────────────────────────────────────

    public ManagementResult Reset()
    {
        _builder.Reset();
        return AutoCommitOrStage("Builder reset to initial state.");
    }

    /// <summary>
    /// Apply a full JSON configuration. Resets the builder and applies
    /// all scalar properties from the JSON. Useful for "paste config and go"
    /// workflows from the dashboard.
    /// </summary>
    public ManagementResult ApplyConfigJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            _builder.Reset();
            RestoreFromJson(json);
            return AutoCommitOrStage("Configuration applied from JSON.");
        }
        catch (Exception ex)
        {
            return ManagementResult.Fail($"Failed to apply config: {ex.Message}");
        }
    }

    // ── Private Helpers ──────────────────────────────────────────────

    /// <summary>
    /// If in a transaction, the change is staged (no pipeline swap).
    /// If not in a transaction, auto-commits immediately.
    /// </summary>
    private ManagementResult AutoCommitOrStage(string message)
    {
        if (_inTransaction)
            return ManagementResult.Ok($"[staged] {message}");

        // Auto-commit: validate, save, then try hot-swap
        var validation = _builder.Validate();
        if (validation.HasCritical)
            return ManagementResult.Ok($"{message} (not committed — validation has critical issues)");

        // Always save config — this is the essential operation. Fail
        // loudly when the write fails so the per-PATCH funnel can't
        // report Ok on an edit that never reached disk.
        var persistError = PersistConfig();
        if (persistError is not null)
            return ManagementResult.Fail($"{message} {FormatPersistFailure(persistError)}");

        // Attempt hot-swap (non-fatal if unavailable)
        var swapped = _result.RebuildFrom(_builder);
        if (swapped)
            return ManagementResult.Ok($"{message} Pipeline rebuilt.");

        // Hot-swap unavailable — try rebuild with downtime
        if (_registration is not null)
            return RebuildWithDowntime();

        return ManagementResult.Ok($"{message} Saved. Pipeline unchanged (restart required).");
    }

    /// <summary>
    /// If ConfigPath is set, writes the current config JSON to disk.
    /// Creates the parent directory if it doesn't exist. Returns the
    /// captured exception when the write fails so callers can surface
    /// "saved" vs "save failed" honestly through <see cref="ManagementResult"/>
    /// rather than reporting success on a vaporised edit.
    ///
    /// <para><b>Why not throw.</b> The previous implementation swallowed
    /// every persistence error and reported <c>Ok</c> regardless. An
    /// operator's last hour of edits disappeared on reboot with no
    /// visible signal. Returning the exception lets each caller decide
    /// whether the failure is fatal to the operation or recoverable
    /// without forcing every PATCH funnel into a try/catch.</para>
    ///
    /// <para><b>Performance.</b> This routes through the lightweight
    /// <see cref="QuickLogBuilder.ExportConfigJsonToFile"/> path —
    /// the JSON is rendered directly from the builder state without
    /// running the full pipeline bootstrap. The earlier path called
    /// <c>ExportConfigToFile</c>, which in turn called <c>Build()</c>
    /// and rebuilt every sink, level registry, and router on every
    /// PATCH. That was the source of the multi-second latency on
    /// per-sink runtime clicks. Callers that actually need a hot-swap
    /// or downtime rebuild trigger one explicitly after PersistConfig
    /// (see <c>CommitFull</c>); the runtime PATCH funnel just needs
    /// the disk write.</para>
    /// </summary>
    /// <returns>
    /// <c>null</c> when there is nothing to persist (no
    /// <see cref="ConfigPath"/>) or the write succeeded; the captured
    /// exception otherwise.
    /// </returns>
    private Exception? PersistConfig()
    {
        if (ConfigPath is null) return null;
        try
        {
            _builder.ExportConfigJsonToFile(ConfigPath);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>
    /// Short, operator-readable failure summary for a persistence
    /// exception. Path comes first so logs/dashboards can group by
    /// the destination file the operator configured.
    /// </summary>
    private string FormatPersistFailure(Exception ex) =>
        $"Configuration save failed for '{ConfigPath}': {ex.GetType().Name}: {ex.Message}";

    /// <summary>
    /// Restore scalar builder properties from a JSON config string.
    /// Only restores properties that are representable in JSON (scalars, not object instances).
    /// </summary>
    private void RestoreFromJson(string json) => RestoreBuilderFromConfig(_builder, json);

    /// <summary>
    /// Apply a full JSON config to a builder. Restores all properties that
    /// the builder knows how to configure: minimum level, sinks, strategy,
    /// async, batching, hot-reload, dynamic levels, sampling, caller info,
    /// trace correlation, level styles, and aliases.
    ///
    /// Call BEFORE Build() to reconstruct a pipeline from a saved config file.
    /// </summary>
    public static void RestoreBuilderFromConfig(QuickLogBuilder builder, string json)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var config = LoggingJsonSerializer.Deserialize(json);

        // Minimum level
        if (!string.IsNullOrEmpty(config.MinimumLevel))
            builder.WithMinimumLevel(config.MinimumLevel);

        // Pipeline-level loopback knobs. The router factory reads
        // these on the next pipeline build to wire the URL / file /
        // bus legs of the loopback interceptor for each sink.
        builder.WithTestLoopbackUrl(config.TestLoopbackUrl);
        builder.WithTestLoopbackLogDir(config.TestLoopbackLogDir);
        builder.WithLoopbackEntriesPerFile(config.LoopbackEntriesPerFile);
        builder.WithLoopbackUseNdjson(config.LoopbackUseNdjson);

        // Sinks restore through SinkEntityPolicy (E2). The branching on
        // sink kind + the file-family bag-vs-legacy resolution still
        // live in the helpers below; the policy is the entry point so
        // every kind in the registry follows one shape.

        // Pipeline strategy
        if (config.PipelineSteps is { Count: > 0 })
        {
            var stepNames = new List<string>();
            foreach (var step in config.PipelineSteps)
                stepNames.Add(step.StepName);
            try { builder.WithPipelineStrategy(Configuration.PipelineStrategy.FromNames(stepNames)); }
            catch { /* invalid step names — keep defaults */ }
        }

        // Async
        if (config.Async is { Enabled: true })
            builder.WithAsyncLogging(config.Async.Capacity, config.Async.DropStrategy);
        else
            builder.WithoutAsyncLogging();

        // Batching
        if (config.Batching is { Enabled: true })
            builder.WithBatching(config.Batching.MaxBatchSize, config.Batching.MaxBatchDelayMs);
        else
            builder.WithoutBatching();

        // Hot reload
        if (config.HotReload is { Enabled: true })
            builder.WithHotReload(config.HotReload.DebounceMs);

        // Dynamic levels
        if (config.DynamicLevels is { Enabled: true })
        {
            builder.WithDynamicLevels();
            if (config.DynamicLevels.CategoryOverrides is not null)
            {
                foreach (var ovr in config.DynamicLevels.CategoryOverrides)
                    builder.WithCategoryLevelOverride(ovr.Category, ovr.LevelKey);
            }
        }

        // Sampling
        if (config.Sampling is { Enabled: true, Rules: not null } && config.Sampling.Rules.Count > 0)
            builder.WithSampling(config.Sampling.Rules[0].SampleRate);
        else
            builder.WithoutSampling();

        // Caller info
        if (config.IncludeCallerInfo)
            builder.WithCallerInfo();
        else
            builder.WithoutCallerInfo();

        // Trace correlation
        if (config.IncludeActivityContext)
            builder.WithTraceCorrelation();

        // Level dump
        if (config.DumpRegisteredLevelsToConsole)
            builder.WithLevelDump();

        // Style collections — levels, categories, properties — restore
        // through the EntityKindRegistry. E1 of the multi-client
        // collaboration design lifts the per-kind restore semantics
        // into IEntityKindPolicy implementations so adding a fourth
        // style kind is "register one policy," not "copy-paste a
        // fourth restore block." Each policy owns its own clear-first
        // semantics (levels upsert; categories / properties clear-
        // then-replay).
        //
        // Boot-time validation: any kind-shaped section in the stored
        // JSON that no registered policy owns is surfaced through
        // EntityKindRegistry.WarnOrphanedSections instead of silently
        // cleared. This closes the PropertyStyles-class bug — a
        // section serialized on save but missing a restore block
        // would round-trip to zero on every load before E1.
        var registry = Entities.EntityKindRegistry.CreateDefault();
        registry.WarnOrphanedSections(registry.FindUnregisteredKindSections(config));
        registry.RestoreAll(builder, config);

        // Custom levels
        if (config.Levels?.AdditionalLevels is not null)
        {
            foreach (var level in config.Levels.AdditionalLevels)
                builder.WithCustomLevel(level.Key, level.DisplayName);
        }

        // Enrichers restore through EnricherEntityPolicy (E3). The
        // per-entry try / catch contract (skip unknown kinds rather
        // than block boot) lives in the policy now.
    }

    /// <summary>
    /// Build the v2 dashboard publish payload for the live file sink.
    /// The payload carries Core-managed metadata (<c>kind</c>,
    /// <c>minLevel</c>, <c>alias</c>) at the top level and the
    /// sink-owned config bag under a <c>properties</c> sub-object.
    /// Every key declared in the registered provider's mmpform
    /// <c>__properties</c> block lands in <c>properties</c>; user-set
    /// values from the inspection win, anything else falls back to
    /// the contract default.
    ///
    /// <para>The "every contract key must appear" guarantee is the
    /// half of the v2 sink-config invariant the dashboard depends on.
    /// QuickLogBuilder's serializer enforces the other half on the
    /// outgoing JSON the live pipeline ran from.</para>
    /// </summary>
    private Dictionary<string, object?> BuildFileSinkConfig(Quick.BuilderInspection inspection)
    {
        var fileKind = inspection.FileKind ?? Services.KnownSinkKinds.TextFile;
        var userValues = ExtractFileSinkUserValues(inspection);

        var provider = _builder.SinkProviders.Get(fileKind);
        var formText = provider?.GetFormSchemaText();
        var contract = Configuration.Sinks.MmpformPropertiesParser.Parse(formText);

        var properties = contract.Count > 0
            ? Configuration.Sinks.SinkPropertyBagBuilder.Build(contract, userValues)
            : (IReadOnlyDictionary<string, object?>)userValues;

        // v2 sub-object — every contract key, defaults filled. New
        // dashboard rendering paths read from here.
        var config = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["properties"] = properties,
        };

        // Transitional flat keys. Older tests + dashboard pages still
        // read the file-sink fields off the top of this dictionary.
        // Removed when every consumer switches to `properties`.
        foreach (var kvp in userValues)
            config[kvp.Key] = kvp.Value;

        return config;
    }

    // Replay the sink's saved runtime override into the builder's
    // SinkRuntimeOverrides map so the next serializer pass writes the
    // same values out again. Skips when the saved sink carries no
    // override fields — keeps backward-compat with snapshots written
    // before the per-sink runtime persist work landed.
    // Internal so SinkEntityPolicy (E2 of the multi-client collaboration
    // design) can reuse the runtime-override apply path without owning
    // a copy of the override-write shape.
    internal static void RestoreSinkRuntimeOverride(QuickLogBuilder builder, Configuration.Json.JsonLogSinkConfig sink)
    {
        var hasOverride = !string.IsNullOrEmpty(sink.RunState)
                          || sink.TeeLiveToFile
                          || sink.TeeLiveToUrl
                          || !string.IsNullOrEmpty(sink.MinLevel);
        if (!hasOverride) return;
        builder.SinkRuntimeOverrides.Set(sink.Kind, new Configuration.Runtime.SinkRuntimeOverride(
            RunState:      sink.RunState,
            TeeLiveToFile: sink.TeeLiveToFile,
            TeeLiveToUrl:  sink.TeeLiveToUrl,
            MinLevel:      sink.MinLevel));
    }

    // Holds the resolved file path + rolling for one sink during the
    // reboot path. `Rolling` is null when the operator disabled it
    // (the bag's rollingLogsEnabled = false) or when the JSON only
    // carries the path with no rolling block.
    // Internal so SinkEntityPolicy can reach the same shape without
    // duplicating the bag-vs-legacy resolution logic.
    internal sealed record ResolvedFileSinkDef(string Path, Configuration.Json.JsonFileRollingConfig? Rolling);

    // Decide which contract describes the saved file-sink definition
    // and turn it into the values WithFileSink needs. The bag wins
    // when present; otherwise the legacy Path + Rolling fields drive.
    internal static ResolvedFileSinkDef? ResolveFileSinkFromConfig(Configuration.Json.JsonLogSinkConfig sink)
    {
        var bag = sink.Properties;
        var hasBag = bag is { Count: > 0 };

        var path = sink.Path;
        var rolling = sink.Rolling;

        if (hasBag)
        {
            var bagPath = BuildPathFromBag(bag!);
            if (!string.IsNullOrEmpty(bagPath))
                path = bagPath;

            if (BagSaysRollingEnabled(bag!))
                rolling = BuildRollingFromBag(bag!, sink.Rolling);
            else if (bag!.ContainsKey("rollingLogsEnabled"))
                rolling = null;     // bag explicitly disables rolling
        }

        return string.IsNullOrEmpty(path) ? null : new ResolvedFileSinkDef(path, rolling);
    }

    private static string? BuildPathFromBag(IReadOnlyDictionary<string, object?> bag)
    {
        var dir = ReadString(bag, "logDirectory");
        var template = ReadString(bag, "logFileTemplate");
        var ext = ReadString(bag, "logExtension")?.TrimStart('.');
        if (string.IsNullOrEmpty(template)) return null;
        if (string.IsNullOrEmpty(ext)) ext = "log";
        return string.IsNullOrEmpty(dir) ? $"{template}.{ext}" : $"{dir.TrimEnd('/')}/{template}.{ext}";
    }

    private static bool BagSaysRollingEnabled(IReadOnlyDictionary<string, object?> bag)
    {
        if (!bag.TryGetValue("rollingLogsEnabled", out var raw) || raw is null) return false;
        if (raw is bool b) return b;
        return bool.TryParse(raw.ToString(), out var parsed) && parsed;
    }

    // Translate the v2 bag into the legacy JsonFileRollingConfig the
    // builder still consumes. Bag values win; fields the v2 contract
    // does not name (logQueueSize, startMinute, custom-window length,
    // locale) fall back to whatever the legacy Rolling block carried
    // so a mixed-shape JSON keeps both halves of its data.
    private static Configuration.Json.JsonFileRollingConfig BuildRollingFromBag(
        IReadOnlyDictionary<string, object?> bag,
        Configuration.Json.JsonFileRollingConfig? legacy)
    {
        var pattern = ReadString(bag, "namePattern") ?? ReadString(bag, "fileNamePattern");
        if (string.IsNullOrEmpty(pattern)) pattern = legacy?.FileNameSuffix;

        return new Configuration.Json.JsonFileRollingConfig(
            Interval: (ReadString(bag, "rollingInterval") ?? legacy?.Interval ?? "daily").ToLowerInvariant(),
            MaxBytes: ParseFileSize(ReadString(bag, "maxFileSize")) ?? legacy?.MaxBytes,
            MaxRetainedFiles: ReadInt(bag, "maxRetainedFiles") ?? legacy?.MaxRetainedFiles,
            LogQueueSize: legacy?.LogQueueSize,
            StartMinute: legacy?.StartMinute ?? 0,
            CaptureDurationMinutes: legacy?.CaptureDurationMinutes ?? 60,
            FileNameSuffix: pattern,
            Locale: legacy?.Locale,
            RetentionDays: ReadInt(bag, "retentionDays") ?? legacy?.RetentionDays,
            TotalSizeCapBytes: ParseFileSize(ReadString(bag, "totalSizeCap")) ?? legacy?.TotalSizeCapBytes);
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> bag, string key) =>
        bag.TryGetValue(key, out var raw) ? raw?.ToString() : null;

    private static int? ReadInt(IReadOnlyDictionary<string, object?> bag, string key)
    {
        if (!bag.TryGetValue(key, out var raw) || raw is null) return null;
        return raw switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)System.Math.Round(d),
            string s when int.TryParse(s, System.Globalization.NumberStyles.Integer,
                                       System.Globalization.CultureInfo.InvariantCulture, out var n) => n,
            _ => (int?)null
        };
    }

    // Build the user-values dictionary that drives the dashboard's
    // file-sink config row. Routes through FileSinkUserValuesBuilder so
    // the publish path and the QuickLogBuilder serializer share one
    // bag-construction body.
    private static Dictionary<string, object?> ExtractFileSinkUserValues(Quick.BuilderInspection inspection) =>
        Configuration.Sinks.FileSinkUserValuesBuilder.From(new Configuration.Sinks.FileSinkInspectionView(
            FilePath:             inspection.FilePath,
            HasFileRolling:       inspection.HasFileRolling,
            FileRollingInterval:  inspection.FileRollingInterval,
            FileMaxBytes:         inspection.FileMaxBytes,
            FileMaxRetainedFiles: inspection.FileMaxRetainedFiles,
            FileNamePattern:      inspection.FileNamePattern,
            TotalSizeCapBytes:    inspection.TotalSizeCapBytes,
            RetentionDays:        inspection.RetentionDays));

    // FormatBytes shim so a couple of remaining call sites in this
    // file still resolve without an import churn. Routes to the
    // canonical helper in FileSinkUserValuesBuilder so the rendering
    // shape stays consistent.
    private static string FormatBytes(long bytes) =>
        Configuration.Sinks.FileSinkUserValuesBuilder.FormatBytes(bytes);

    // Pull the `configContract` integer out of the sink's
    // CAPABILITY.yaml without dragging in a YAML parser. The manifest
    // is short and the field, when present, sits as a simple top-level
    // `configContract: <n>` line. Returns 1 (legacy) on any read /
    // parse failure and on absence — any sink that hasn't migrated
    // keeps working unchanged.
    private int ReadConfigContract(string sinkKind)
    {
        var yaml = _builder.SinkProviders.Get(sinkKind)?.GetCapabilityYaml();
        if (string.IsNullOrEmpty(yaml)) return 1;

        foreach (var line in yaml.Split('\n'))
        {
            var trimmed = line.AsSpan().TrimStart();
            if (trimmed.IsEmpty || trimmed[0] == '#') continue;
            const string Marker = "configContract:";
            if (!trimmed.StartsWith(Marker, StringComparison.Ordinal)) continue;
            var rest = trimmed.Slice(Marker.Length).Trim();
            // Strip trailing inline comment if any.
            var hash = rest.IndexOf('#');
            if (hash >= 0) rest = rest.Slice(0, hash).Trim();
            return int.TryParse(rest, out var n) ? n : 1;
        }
        return 1;
    }
}

/// <summary>Result of a management API operation.</summary>
public sealed record ManagementResult(bool Success, string Message)
{
    public static ManagementResult Ok(string message) => new(true, message);
    public static ManagementResult Fail(string message) => new(false, message);
}

/// <summary>
/// Serializable channel definition for REST API consumption.
/// Maps to actual ChannelDefinition internally.
/// </summary>
public sealed record ChannelInfo(
    string Name,
    string OutputKind,     // "console", "file"
    string? Path,          // file path for file-based outputs
    string? MinLevel);     // optional per-channel minimum level

/// <summary>Runtime state snapshot of the live pipeline.</summary>
public sealed record PipelineRuntimeState(
    int? AsyncQueueDepth,
    int? AsyncCapacity,
    string? AsyncDropStrategy,
    string? CircuitBreakerState,
    int? CircuitBreakerFailures,
    int? FilterCount,
    int? SinkCount,
    bool IsTransactionActive);

/// <summary>
/// Describes a registered sink provider — its kind, display name, and
/// configuration schema (if it implements IConfigurableSinkProvider).
/// The dashboard uses this to render plugin configuration forms dynamically.
/// </summary>
public sealed record SinkProviderInfo(
    string SinkKind,
    string DisplayName,
    string? Description,
    bool IsConfigurable,
    System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField>? Schema,
    System.Collections.Generic.IReadOnlyDictionary<string, object?>? CurrentConfig);

/// <summary>
/// Describes a registered pipeline decorator plugin — its step name, display name,
/// and configuration schema.
/// </summary>
public sealed record PipelineDecoratorInfo(
    string StepName,
    string DisplayName,
    string Description,
    System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> Schema,
    System.Collections.Generic.IReadOnlyDictionary<string, object?> CurrentConfig);

/// <summary>
/// The pipeline flow: ordered steps + sinks under FanOut + the
/// pipeline-level loopback knobs the dashboard's Configuration tab
/// renders at the top of the form.
/// </summary>
public sealed record PipelineFlowInfo(
    System.Collections.Generic.IReadOnlyList<PipelineFlowStep> Steps,
    System.Collections.Generic.IReadOnlyList<PipelineFlowSink> Sinks,
    string? TestLoopbackUrl = null,
    string? TestLoopbackLogDir = null,
    int LoopbackEntriesPerFile = 1000,
    bool LoopbackUseNdjson = true);

/// <summary>
/// A pipeline step reference. In flow responses, only StepName and Alias are populated.
/// The full metadata (DisplayName, Description, Help, Vendor) comes from the global
/// known steps endpoint and is merged client-side.
/// </summary>
public sealed record PipelineFlowStep(
    string StepName,
    string? Alias = null,
    string DisplayName = "",
    string Description = "",
    string Help = "",
    Pipeline.VendorInfo? Vendor = null,
    string LinkType = "middle");

/// <summary>A sink registered under the FanOut step.</summary>
/// <remarks>
/// <para><b>ConfigContract.</b> Mirrors the <c>configContract</c> field on
/// the sink's CAPABILITY.yaml. The dashboard reads this number to decide
/// which commit shape to send: 1 = legacy flat <c>config: { … }</c>;
/// 2 = v2 sub-object <c>config: { properties: { … } }</c>. Defaults to
/// 1 when the manifest omits the field, so any sink that hasn't migrated
/// keeps working unchanged.</para>
/// </remarks>
public sealed record PipelineFlowSink(
    string SinkId,
    string DisplayName,
    string Description,
    string? MinLevel,
    string Help = "",
    Pipeline.VendorInfo? Vendor = null,
    string? Alias = null,
    System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField>? Schema = null,
    System.Collections.Generic.Dictionary<string, object?>? Config = null,
    int ConfigContract = 1);

/// <summary>
/// Describes a child item within a container step (filters, processors, sinks).
/// Used by the dashboard to render expandable child lists under container steps.
/// </summary>
public sealed record PipelineChildInfo(
    string ChildType,
    string DisplayName,
    string Description,
    string Icon = "extension");
