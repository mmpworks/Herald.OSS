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
/// <para><b>Internal shape.</b> This type is a thin facade. The method
/// bodies live on four per-axis collaborators reached through the
/// <see cref="IManagementContext"/> interface this class implements:
/// <see cref="SinkManagement"/>, <see cref="LevelManagement"/>,
/// <see cref="PolicyManagement"/>, and <see cref="TransactionScope"/>.
/// The facade owns the authorizer, the transaction state, and the
/// persistence target so every axis routes through one gate (Glenn 1
/// queue #1) and one persistence funnel (queue #3).</para>
///
/// Usage:
///   var api = new HeraldManagementApi(builder, result);
///   api.BeginTransaction();
///   api.SetMinimumLevel("debug");
///   api.SetPipelineStrategy("filterEarly");
///   api.CommitTransaction(); // atomic swap
/// </summary>
public sealed class HeraldManagementApi : IManagementContext
{
    private readonly QuickLogBuilder _builder;
    private QuickLogResult _result;
    private readonly HeraldRegistration? _registration;
    private string? _snapshot; // JSON snapshot for rollback
    private bool _inTransaction;
    private IManagementApiAuthorizer _authorizer;

    // Per-axis collaborators. Created in the constructor with `this`
    // passed as IManagementContext so each axis routes its shared-state
    // reads / writes through one well-defined surface. The transaction
    // axis also takes the sink axis directly because CommitFull
    // dispatches through SinkManagement.ApplySinkConfig per-sink.
    private readonly SinkManagement _sinks;
    private readonly LevelManagement _levels;
    private readonly PolicyManagement _policies;
    private readonly TransactionScope _transactions;

    public HeraldManagementApi(QuickLogBuilder builder, QuickLogResult result)
        : this(builder, result, authorizer: null)
    {
    }

    /// <summary>
    /// Construct a management API with an explicit authorizer. The
    /// OSS default is <see cref="RejectAllAuthorizer"/> — a host that
    /// hasn't wired authentication can't be tricked into mutating its
    /// pipeline. Pass <see cref="AllowAllAuthorizer.Instance"/> for a
    /// deliberately-unauthenticated test harness or CLI tool.
    /// </summary>
    public HeraldManagementApi(
        QuickLogBuilder builder,
        QuickLogResult result,
        IManagementApiAuthorizer? authorizer)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _authorizer = authorizer ?? RejectAllAuthorizer.Instance;

        // Wire the per-axis collaborators. `this` is IManagementContext,
        // so each axis sees the same shared state. The transaction
        // scope takes the sink axis directly because CommitFull
        // dispatches per-sink through SinkManagement.ApplySinkConfig.
        _sinks = new SinkManagement(this);
        _levels = new LevelManagement(this);
        _policies = new PolicyManagement(this);
        _transactions = new TransactionScope(this, _sinks);
    }

    /// <summary>
    /// The authorizer invoked at the head of every mutating method.
    /// Set this to replace the default <see cref="RejectAllAuthorizer"/>
    /// at any point in the API's lifetime — useful when authentication
    /// is wired after the host has already started.
    /// </summary>
    public IManagementApiAuthorizer Authorizer
    {
        get => _authorizer;
        set => _authorizer = value ?? RejectAllAuthorizer.Instance;
    }

    /// <summary>
    /// Authorization gate invoked at the head of every mutating
    /// method. Returns <c>null</c> when the operation is allowed;
    /// returns a populated <see cref="ManagementResult.Fail"/> when
    /// the authorizer denies it so the caller can early-return.
    /// </summary>
    ManagementResult? IManagementContext.EnsureAuthorized(string operation)
    {
        if (_authorizer.IsAuthorized(operation, out var reason)) return null;
        return ManagementResult.Fail(reason ?? $"Operation '{operation}' was denied by the authorizer.");
    }

    /// <summary>
    /// Create a management API from a registry entry, inheriting its ConfigPath
    /// so commits auto-persist for persistent pipelines. The authorizer
    /// defaults to <see cref="RejectAllAuthorizer"/>; pass one explicitly to
    /// avoid the rejecting default.
    /// </summary>
    public static HeraldManagementApi FromRegistration(HeraldRegistration entry, IManagementApiAuthorizer? authorizer = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new HeraldManagementApi(entry.Builder, entry.Result, entry, authorizer) { ConfigPath = entry.ConfigPath };
    }

    private HeraldManagementApi(QuickLogBuilder builder, QuickLogResult result, HeraldRegistration? registration, IManagementApiAuthorizer? authorizer = null)
        : this(builder, result, authorizer)
    {
        _registration = registration;
    }

    /// <summary>
    /// When set, the pipeline config is persisted to this file path after every
    /// successful commit (both auto-commit and transactional commit).
    /// </summary>
    public string? ConfigPath { get; set; }

    /// <summary>
    /// When set, every file-sink path supplied through this API must
    /// resolve inside this directory. Paths that escape — via absolute
    /// override, parent-directory references, or symlink-adjacent
    /// tricks on the textual path — are rejected with
    /// <see cref="ManagementResult.Fail"/> rather than wired into the
    /// pipeline.
    ///
    /// <para>
    /// <b>Default <c>null</c>:</b> the file-sink path is accepted
    /// unchanged for source-compatibility with pre-1.0 callers, but a
    /// <see cref="Diagnostics.HeraldRuntimeMessages"/>
    /// <see cref="Diagnostics.NoticeSeverity.Warning"/> is published
    /// every time so the operator sees the gap before exposing the
    /// API over HTTP. The recommended deployment shape is to set this
    /// to a tenant-scoped log directory at construction time and
    /// leave it set for the life of the host.
    /// </para>
    /// </summary>
    public string? LogRootDirectory { get; set; }

    /// <summary>
    /// Validate <paramref name="path"/> for a Management-API file-sink
    /// call. Returns the resolved-and-confined absolute path on
    /// success; returns the original path + emits a runtime-notice
    /// warning when <see cref="LogRootDirectory"/> is not configured
    /// (legacy pass-through). Throws <see cref="InvalidOperationException"/>
    /// when the path escapes the configured root.
    ///
    /// <para>
    /// Callers handle the throw by translating it into
    /// <see cref="ManagementResult.Fail"/> — the principal review's
    /// "reject via ManagementResult.Fail, not exception" rule applies
    /// at the public API boundary, not at this internal helper.
    /// </para>
    /// </summary>
    string IManagementContext.ResolveFileSinkPath(string path)
    {
        var root = LogRootDirectory;
        if (string.IsNullOrWhiteSpace(root))
        {
            // Legacy pass-through. Surface a runtime-notice warning so
            // an operator running diagnostics sees that the file-sink
            // path was not confined; this is the signal that flips a
            // hardened production deployment into compliance.
            Diagnostics.HeraldRuntimeMessages.Publish(
                source: nameof(HeraldManagementApi),
                message: $"File-sink path '{path}' wired without a configured LogRootDirectory. " +
                         "Set HeraldManagementApi.LogRootDirectory to confine file writes before exposing this API over HTTP.",
                severity: Diagnostics.NoticeSeverity.Warning);
            return path;
        }

        // ConfinedPathResolver canonicalises both root and the
        // candidate path before comparing prefixes, so .. segments
        // collapse before any "starts with root" check. An escape
        // throws InvalidOperationException which the caller catches
        // and reports through ManagementResult.Fail.
        var resolver = new Output.Writers.ConfinedPathResolver(root);
        return resolver.Resolve(path);
    }

    // ── IManagementContext plumbing ───────────────────────────────────

    QuickLogBuilder IManagementContext.Builder => _builder;
    QuickLogResult IManagementContext.Result => _result;
    HeraldRegistration? IManagementContext.Registration => _registration;
    void IManagementContext.ReplaceResult(QuickLogResult newResult) => _result = newResult;
    bool IManagementContext.InTransaction => _inTransaction;
    void IManagementContext.SetInTransaction(bool value) => _inTransaction = value;
    string? IManagementContext.Snapshot { get => _snapshot; set => _snapshot = value; }
    string? IManagementContext.ConfigPath => ConfigPath;
    string? IManagementContext.LogRootDirectory => LogRootDirectory;
    ManagementResult IManagementContext.AutoCommitOrStage(string message) => _transactions.AutoCommitOrStage(message);
    ManagementResult IManagementContext.RebuildWithDowntime() => _transactions.RebuildWithDowntime();
    void IManagementContext.RestoreFromJson(string json) => RestoreBuilderFromConfig(_builder, json);

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
    Exception? IManagementContext.PersistConfig()
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
    string IManagementContext.FormatPersistFailure(Exception ex) =>
        $"Configuration save failed for '{ConfigPath}': {ex.GetType().Name}: {ex.Message}";

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
        string sinkKind, IReadOnlyDictionary<string, object?> values) =>
        _sinks.ConfigureSinkProvider(sinkKind, values);

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
        string stepName, IReadOnlyDictionary<string, object?> values) =>
        _policies.ConfigurePipelineDecorator(stepName, values);

    // ── Per-sink runtime apply ────────────────────────────────────────

    /// <summary>
    /// Single funnel for every per-sink runtime change the dashboard's
    /// strip can fire (run state + tee flags + sink-level minimum).
    /// Updates the in-memory holders so the next event respects the
    /// new value, mirrors the change into the builder's
    /// SinkRuntimeOverrides map, then persists the JSON so a reboot
    /// sees the same state. CUPID + DRY: every endpoint and helper
    /// that touches per-sink runtime routes through here, so there
    /// is exactly one persistence path to reason about.
    /// </summary>
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
        Configuration.Runtime.SinkRuntimeOverride incoming) =>
        _sinks.ApplySinkRuntime(pipelineName, sinkId, incoming);

    // ── Full commit (dashboard bulk save) ─────────────────────────────

    /// <summary>
    /// Apply a complete pipeline configuration from the Dashboard.
    /// Receives steps (with configs), sinks (with configs), and aliases in one pass.
    /// Applies everything to the builder, saves to disk, then attempts hot-swap.
    /// </summary>
    public ManagementResult CommitFull(string json) => _transactions.CommitFull(json);

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
    public ManagementResult RebuildWithDowntime() => _transactions.RebuildWithDowntime();

    // ── Transaction Operations ───────────────────────────────────────

    /// <summary>
    /// Begin a transaction. Snapshots the current builder state as JSON.
    /// All subsequent changes are staged until Commit() or Rollback().
    /// </summary>
    public ManagementResult BeginTransaction() => _transactions.BeginTransaction();

    /// <summary>
    /// Commit the transaction: rebuild the pipeline from the current builder state
    /// and swap atomically via SwappableLogger.
    /// </summary>
    public ManagementResult CommitTransaction() => _transactions.CommitTransaction();

    /// <summary>
    /// Rollback the transaction: restore the builder to the snapshot taken at Begin().
    /// No pipeline change occurs.
    /// </summary>
    public ManagementResult RollbackTransaction() => _transactions.RollbackTransaction();

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

    // ── Write Operations: Scalars (level + sinks) ────────────────────

    public ManagementResult SetMinimumLevel(string level) => _levels.SetMinimumLevel(level);

    public ManagementResult SetConsoleSink(bool enabled, string? minLevel = null) =>
        _sinks.SetConsoleSink(enabled, minLevel);

    public ManagementResult UpdateConsoleMinLevel(string? minLevel) =>
        _sinks.UpdateConsoleMinLevel(minLevel);

    public ManagementResult SetFileSink(bool enabled, string? path = null, string? minLevel = null) =>
        _sinks.SetFileSink(enabled, path, minLevel);

    public ManagementResult UpdateFileMinLevel(string? minLevel) =>
        _sinks.UpdateFileMinLevel(minLevel);

    /// <summary>
    /// Update the file sink's retention policy (retention days and/or total size cap).
    /// File sink must already be configured.
    /// </summary>
    public ManagementResult UpdateFileRetentionPolicy(int? retentionDays = null, long? totalSizeCapBytes = null) =>
        _sinks.UpdateFileRetentionPolicy(retentionDays, totalSizeCapBytes);

    public ManagementResult SetPipelineStrategy(string strategyName) =>
        _policies.SetPipelineStrategy(strategyName);

    public ManagementResult SetPipelineStrategyCustom(IReadOnlyList<string> stepNames) =>
        _policies.SetPipelineStrategyCustom(stepNames);

    public ManagementResult SetTraceCorrelation(bool enabled) =>
        _policies.SetTraceCorrelation(enabled);

    public ManagementResult SetLevelDump(bool enabled) =>
        _policies.SetLevelDump(enabled);

    // ── Write Operations: Custom Levels ────────────────────────────

    /// <summary>Get all custom (non-base) log levels.</summary>
    public IReadOnlyList<Levels.LogLevel> GetCustomLevels() => _levels.GetCustomLevels();

    /// <summary>Add or update a custom log level.</summary>
    public ManagementResult AddCustomLevel(string key, string displayName) =>
        _levels.AddCustomLevel(key, displayName);

    /// <summary>Remove a custom log level by key. Base levels cannot be removed.</summary>
    public ManagementResult RemoveCustomLevel(string key) => _levels.RemoveCustomLevel(key);

    /// <summary>Remove all custom log levels.</summary>
    public ManagementResult ClearCustomLevels() => _levels.ClearCustomLevels();

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
    public ManagementResult SetLevelOrder(IEnumerable<string>? keys) => _levels.SetLevelOrder(keys);

    // ── Write Operations: Level Styles ─────────────────────────────

    /// <summary>
    /// Get the current level styles (merged defaults + overrides).
    /// Returns the effective style for each level: key, color, bold, italic, background.
    /// </summary>
    public IReadOnlyList<Quick.LevelStyleInfo> GetLevelStyles() => _levels.GetLevelStyles();

    /// <summary>
    /// Set or override the display style for a log level.
    /// </summary>
    public ManagementResult SetLevelStyle(string levelKey, string colorName,
        bool bold = false, bool italic = false, string? backgroundColorName = null) =>
        _levels.SetLevelStyle(levelKey, colorName, bold, italic, backgroundColorName);

    /// <summary>Remove a level style override (revert to default for that level).</summary>
    public ManagementResult RemoveLevelStyle(string levelKey) => _levels.RemoveLevelStyle(levelKey);

    /// <summary>Clear all level style overrides (revert to defaults).</summary>
    public ManagementResult ClearLevelStyles() => _levels.ClearLevelStyles();

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
        bool? deferRendering = null) =>
        _policies.SetAsyncConfig(enabled, capacity, dropStrategy, deferRendering);

    /// <summary>
    /// Configure the batching step. Pass <paramref name="enabled"/>=false
    /// to disable batching; the size/delay parameters are ignored in that
    /// case. When enabled, both size and delay must be positive.
    /// </summary>
    public ManagementResult SetBatchingConfig(
        bool enabled,
        int? maxBatchSize = null,
        int? maxBatchDelayMs = null) =>
        _policies.SetBatchingConfig(enabled, maxBatchSize, maxBatchDelayMs);

    /// <summary>
    /// Set the random sampling rate (1-in-N). A rate of 0 disables
    /// sampling entirely. Negative rates are rejected.
    /// </summary>
    public ManagementResult SetSamplingRate(int rate) => _policies.SetSamplingRate(rate);

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
        string? triggerLevel = null) =>
        _policies.SetFlightRecorderConfig(enabled, bufferSize, minLevel, triggerLevel);

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
        int? maxBatchDelayMs = null) =>
        _policies.SetPostFilteringConfig(enabled, triggerCondition, normalFilter, escalatedFilter, maxBatchSize, maxBatchDelayMs);

    // ── Write Operations: Enrichers ──────────────────────────────────

    public ManagementResult RemoveEnricher(string name) => _policies.RemoveEnricher(name);

    public ManagementResult ClearEnrichers() => _policies.ClearEnrichers();

    public ManagementResult ResetEnrichers() => _policies.ResetEnrichers();

    // ── Write Operations: Event Processors ───────────────────────────

    public ManagementResult RemoveEventProcessor(string name) => _policies.RemoveEventProcessor(name);

    public ManagementResult ClearEventProcessors() => _policies.ClearEventProcessors();

    // ── Write Operations: Property Styles ────────────────────────────

    public ManagementResult SetPropertyStyle(string propertyName, string colorName,
        bool bold = false, bool italic = false, string? backgroundColor = null) =>
        _policies.SetPropertyStyle(propertyName, colorName, bold, italic, backgroundColor);

    public ManagementResult RemovePropertyStyle(string propertyName) =>
        _policies.RemovePropertyStyle(propertyName);

    public ManagementResult ClearPropertyStyles() => _policies.ClearPropertyStyles();

    // -- Write Operations: Category (Channel) Styles ------------------

    /// <summary>
    /// Get the current category (channel) styles from the builder snapshot.
    /// Returns per-category colour / bold / italic / background; the list is
    /// empty when no category styles are configured. The presentation-side
    /// term is "channel"; the wire term is "category" so it matches the
    /// <c>LogEvent.Category</c> dimension.
    /// </summary>
    public IReadOnlyList<Quick.CategoryStyleInfo> GetCategoryStyles() => _policies.GetCategoryStyles();

    /// <summary>Set or replace the display style for a log category.</summary>
    public ManagementResult SetCategoryStyle(string categoryName, string? colorName = null,
        bool bold = false, bool italic = false, string? backgroundColorName = null) =>
        _policies.SetCategoryStyle(categoryName, colorName, bold, italic, backgroundColorName);

    /// <summary>Remove a category style override.</summary>
    public ManagementResult RemoveCategoryStyle(string categoryName) =>
        _policies.RemoveCategoryStyle(categoryName);

    /// <summary>Clear all category style overrides.</summary>
    public ManagementResult ClearCategoryStyles() => _policies.ClearCategoryStyles();

    // ── Write Operations: Aliases ─────────────────────────────────────

    /// <summary>Get all aliases.</summary>
    public IReadOnlyDictionary<string, string> GetAliases() => _policies.GetAliases();

    /// <summary>Set an alias for a pipeline step, sink, or filter.</summary>
    public ManagementResult SetAlias(string id, string alias) => _policies.SetAlias(id, alias);

    /// <summary>Remove an alias.</summary>
    public ManagementResult RemoveAlias(string id) => _policies.RemoveAlias(id);

    // ── Write Operations: Channels ───────────────────────────────────

    /// <summary>Get all configured channels.</summary>
    public IReadOnlyList<ChannelInfo> GetChannels() => _sinks.GetChannels();

    /// <summary>Add a new channel by kind.</summary>
    public ManagementResult AddChannel(ChannelInfo channel) => _sinks.AddChannel(channel);

    public ManagementResult RemoveChannel(string channelName) => _sinks.RemoveChannel(channelName);

    public ManagementResult ClearChannels() => _sinks.ClearChannels();

    // ── Write Operations: Bulk ───────────────────────────────────────

    public ManagementResult Reset() => _transactions.Reset();

    /// <summary>
    /// Apply a full JSON configuration. Resets the builder and applies
    /// all scalar properties from the JSON. Useful for "paste config and go"
    /// workflows from the dashboard.
    /// </summary>
    public ManagementResult ApplyConfigJson(string json) => _transactions.ApplyConfigJson(json);

    // ── Boot-time restore (static) ───────────────────────────────────

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

    // ── File-sink config builders + boot-restore helpers ─────────────
    //
    // These helpers serve two callers: the publish path (GetPipelineFlow
    // builds the v2 sink-config payload) and the boot-restore path
    // (SinkEntityPolicy reaches the internal entry points to replay a
    // saved file-sink). They sit on the facade because the publish path
    // needs builder + provider state in one place; the static internal
    // entry points stay for SinkEntityPolicy's call signature.

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
            MaxBytes: SinkManagement.ParseFileSize(ReadString(bag, "maxFileSize")) ?? legacy?.MaxBytes,
            MaxRetainedFiles: ReadInt(bag, "maxRetainedFiles") ?? legacy?.MaxRetainedFiles,
            LogQueueSize: legacy?.LogQueueSize,
            StartMinute: legacy?.StartMinute ?? 0,
            CaptureDurationMinutes: legacy?.CaptureDurationMinutes ?? 60,
            FileNameSuffix: pattern,
            Locale: legacy?.Locale,
            RetentionDays: ReadInt(bag, "retentionDays") ?? legacy?.RetentionDays,
            TotalSizeCapBytes: SinkManagement.ParseFileSize(ReadString(bag, "totalSizeCap")) ?? legacy?.TotalSizeCapBytes);
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

/// <summary>
/// Result of a management API operation.
///
/// <para>
/// <b>Why <see cref="Kind"/> is on the result.</b> Authorization
/// denials carry a typed <see cref="DenialKind"/> from
/// <see cref="AuthorizationDecision.Kind"/>. Surfacing that kind on
/// the result lets the Dashboard renderer and the audit log switch
/// on a named instance instead of recovering it from
/// <see cref="Message"/> text. The field defaults to null so every
/// pre-existing call site (<c>Fail("...")</c>, every <c>Ok(...)</c>)
/// stays source-compatible.
/// </para>
/// </summary>
public sealed record ManagementResult(bool Success, string Message, DenialKind? Kind = null)
{
    /// <summary>Successful result with an operator-readable summary.</summary>
    public static ManagementResult Ok(string message) => new(true, message);

    /// <summary>
    /// Failure result. The optional <paramref name="kind"/> carries
    /// the named denial category through to renderers that switch on
    /// <see cref="DenialKind"/> instead of parsing
    /// <paramref name="reason"/> text. Existing call sites that pass
    /// only the reason continue to compile and produce a result with
    /// <see cref="Kind"/> = <see langword="null"/>.
    /// </summary>
    public static ManagementResult Fail(string reason, DenialKind? kind = null) => new(false, reason, kind);
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
