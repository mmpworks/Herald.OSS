#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using MMP.Herald.Bootstrap;
using MMP.Herald.Configuration;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Output.Rich;
using MMP.Herald.Pipeline;
using MMP.Herald.Predicates;
using MMP.Herald.Routing;
using MMP.Herald.Signals;
using MMP.Herald.Time;

namespace MMP.Herald.Quick;

/// <summary>
/// Fluent builder for quick logging setup without JSON configuration files.
/// Builds a JsonLoggingConfig programmatically and feeds it through the
/// existing bootstrap pipeline, so all logging features remain available.
///
/// <para>
/// <b>Thread-safety:</b> <see cref="QuickLogBuilder"/> and its collection
/// properties (<see cref="Enrichers"/>, <see cref="EventProcessors"/>,
/// <see cref="PropertyStyles"/>, etc.) are intended for single-threaded
/// construction. Fluent configuration happens on the calling thread; once
/// <see cref="Build"/> or <see cref="BuildAndCommit"/> returns, the resulting
/// <see cref="QuickLogResult"/> is what participates in the concurrent
/// logging pipeline. Sharing a builder instance across threads during
/// configuration is unsupported.
/// </para>
///
/// Split across partial files for readability:
///   QuickLogBuilder.cs           - fields, Create(), Build(), private helpers
///   QuickLogBuilder.With.cs      - With*() fluent configuration methods
///   QuickLogBuilder.Mutations.cs - Get*, Replace*, Set*, Update*, Without*, Clear*, Reset
///   QuickLogBuilder.Diagnostics.cs - Inspect(), Validate(), ExportConfig()
///
/// Usage:
///   var result = QuickLogBuilder.Create()
///       .WithConsoleSink()
///       .WithMinimumLevel("debug")
///       .Build();
/// </summary>
public sealed partial class QuickLogBuilder
{
    private string _minimumLevel = Services.LogLevelKeys.Information;
    private bool _includeConsole;
    private string? _consoleMinLevel;
    private bool _includeNullSink;
    private string? _nullSinkMinLevel;
    private bool _dumpLevels;
    private string? _logFilePath;
    private string? _logFileMinLevel;
    private string _logFileKind = Services.KnownSinkKinds.TextFile;
    private JsonFileRollingConfig? _logFileRolling;
    private bool _includeActivityContext;
    private bool _includeCallerInfo;
    private IRenderedLogOutputWriter? _consoleWriter;
    private ILogSignalHandler? _globalSignalHandler;
    private IDateTimeProvider? _timeProvider;
    private Configuration.PipelineStrategy? _pipelineStrategy;
    private List<Configuration.Json.JsonLogLevelStyleConfig>? _levelStyleOverrides;
    private Dictionary<string, string>? _aliases;
    private readonly List<Pipeline.IConfigurablePipelineDecorator> _pipelineDecorators = new();

    // Fast-path redaction rules. Held on the builder; compiled into a
    // FastPathRedactor and installed on the StructuredLogger after Build()
    // returns. Stays out of EventProcessors so the kernel fast path stays
    // eligible — see FastPathRedactor.cs for the design rationale.
    private List<Pipeline.Processors.CompiledRedactionRule>? _fastRedactionRules;

    // Fast-path sample rate. Same lifecycle as the redaction rules —
    // installed on the StructuredLogger after Build() returns; stays out
    // of LogPipelinePolicy.SamplingFilter so the kernel fast path stays
    // eligible. Null = no sampling configured. See FastPathSampler.cs.
    private int? _fastSampleRate;

    // Fast-path static enricher — properties to append to every accepted
    // event. Same lifecycle as the other kernel-aware companions. See
    // FastPathEnricher.cs for the design rationale.
    private List<MMP.Herald.Templating.LogProperty>? _fastEnrichmentProperties;

    // Fast-path dynamic level switch — when set, every accepted-by-static-
    // level event also passes through this resolver, which reads the
    // current level from a LogLevelSwitch (mutable at runtime). Stays out
    // of the legacy DynamicLevelPolicy slot so the kernel stays eligible.
    private MMP.Herald.Levels.LogLevelSwitch? _fastDynamicLevelSwitch;

    // Optional per-category override map paired with the global switch
    // above. Null = global-only (the dominant case). When configured,
    // categories with overrides take precedence over the global switch
    // for matching events; other categories fall through to the global
    // switch. Mutable at runtime via the map's own SetCategoryLevel /
    // RemoveCategoryOverride methods.
    private MMP.Herald.Levels.CategoryLevelSwitchMap? _fastDynamicLevelCategoryMap;

    // Bounded capacity for the kernel-aware async sink wrapper. Null =
    // no async wrapping (the kernel fans out to user sinks directly,
    // sync). When set, post-build wraps the routed-sinks composite in a
    // single FastPathAsyncSink with this capacity so the producer
    // returns immediately; a background consumer drains and forwards.
    private int? _fastAsyncSinkCapacity;

    // Destructuring policies for {@Name} structured capture. Registered in
    // order; the first policy to return true wins. Threaded through to
    // DestructuringPolicyRegistry at bootstrap.
    private readonly List<MMP.Herald.Templating.IDestructuringPolicy> _destructuringPolicies = new();

    // Property-naming policy. Drives how typed-args call-site names get
    // emitted as property names on every event. Null means "use the spec
    // default" — PascalCasePolicy. Round-trips through JSON via the
    // NamingPolicy string id on JsonLoggingConfig. RebuildFrom carries
    // this reference forward by default (set on the rebuilt-from-builder
    // mutation explicitly).
    private MMP.Herald.Templating.IPropertyNamingPolicy? _namingPolicy;

    // First-dispatch naming-policy announcement suppression (Phase 5).
    // When true, the eventual StructuredLogger does not emit the one-shot
    // "Active naming policy: ..." Info event. Useful for embedded /
    // headless callers who don't want the message in their sinks. The
    // env var HERALD_NAMINGPOLICY_QUIET=1 is a process-wide alternative
    // honoured directly by StructuredLogger.
    private bool _suppressNamingPolicyAnnouncement;

    // Per-sink label overrides keyed by sink name. The security registrar
    // identifies sinks by label, not by config name; passing an empty
    // string here keeps the entry but lets the gate's auto-generator pick
    // a random 32-hex-char label at hoist time. Sinks not present in the
    // map get null and the gate falls back to auto-generation.
    private readonly Dictionary<string, string?> _sinkLabels =
        new(System.StringComparer.OrdinalIgnoreCase);

    // Plug-point for rendering destructured projections. Defaults to the
    // AOT-clean ToStringComplexValueSerializer; consumers install a plugin
    // (e.g. MMP.Herald.Plugins.Serialization.Reflection) and override via
    // WithComplexValueSerializer(...). Threaded through to every
    // TransformDestructuringPolicy<T> created via Destructure<T>.
    private MMP.Herald.Templating.IComplexValueSerializer _complexValueSerializer =
        MMP.Herald.Templating.ToStringComplexValueSerializer.Instance;

    // Pipeline policy fields
    private bool _asyncEnabled;
    private int _asyncCapacity = Services.PipelineDefaults.AsyncCapacity;
    private string _asyncDropStrategy = Services.KnownDropStrategies.DropWrite;
    private bool _asyncDeferRendering;
    private bool _batchingEnabled;
    private int _batchMaxSize = Services.PipelineDefaults.BatchSize;
    private int _batchDelayMs = Services.PipelineDefaults.BatchDelayMs;
    private bool _hotReloadEnabled;
    private int _hotReloadDebounceMs = 500;
    private bool _dynamicLevelsEnabled;
    private Dictionary<string, string>? _categoryLevelOverrides;
    private Filters.ILogFilter? _samplingFilter;
    private int _samplingRate;
    // Accumulated sampling/throttling/adaptive rules. Composes into a single
    // CompositeSamplingFilter at Build (the mapper picks the runtime filter per rule).
    // WithSampling/WithThrottling/WithAdaptiveSampling append here; the Build emit at
    // BuildJsonConfig serializes the list into JsonSamplingConfig.Rules. Stays empty on
    // the common no-sampling path so the JSON shape is unchanged for those pipelines.
    private System.Collections.Generic.List<Configuration.Json.JsonSamplingRule>? _samplingRules;
    private List<Levels.LogLevel>? _customLevels;

    // Flight-recorder ring buffer (JsonFlightRecorderConfig). All fields stay
    // dormant until WithFlightRecorder enables it, so a builder that never
    // touches the recorder never emits the JSON block.
    private bool _flightRecorderEnabled;
    private int _flightRecorderBufferSize = 200;
    private string? _flightRecorderMinLevel;
    private string? _flightRecorderTriggerLevel;

    // Post-filtering batch (JsonPostFilteringConfig). The three predicates are
    // owned by the caller; the builder just stores them so BuildJsonConfig can
    // round-trip the same shape that loaded from JSON.
    private bool _postFilteringEnabled;
    private PredicateSpec? _postFilteringTrigger;
    private PredicateSpec? _postFilteringNormal;
    private PredicateSpec? _postFilteringEscalated;
    private int _postFilteringMaxBatchSize = Services.PipelineDefaults.BatchSize;
    private int _postFilteringMaxBatchDelayMs = Services.PipelineDefaults.BatchDelayMs;

    // Optional level-key ordering override. When set, BuildJsonConfig
    // emits baseLevels in this exact order (drawn from the canonical
    // six plus any custom levels added via WithCustomLevel). Drives
    // the dashboard's drag-rearrange flow: the Categories tab posts a
    // new key list, the management API stores it here, RebuildFrom
    // hot-reloads the pipeline, and the new ordering produces a fresh
    // rank assignment that LevelFilter honours.
    private List<string>? _levelOrder;

    // Pipeline-level loopback knobs. Threaded through into
    // JsonLoggingConfig + LoggingRuntimeConfiguration so the
    // LoopbackInterceptor can find the URL / log dir / format flags
    // for the URL + file legs. Defaults preserve the pre-loopback
    // behaviour: no URL, no log dir, NDJSON format when any leg
    // does fire, 1000-entry rotation cap.
    private string? _testLoopbackUrl;
    private string? _testLoopbackLogDir;
    private int _loopbackEntriesPerFile = 1000;
    private bool _loopbackUseNdjson = true;

    /// <summary>Pipeline's loopback URL, or null when no URL leg is configured.</summary>
    public string? TestLoopbackUrl => _testLoopbackUrl;
    /// <summary>Pipeline's loopback log directory, or null when no file leg is configured.</summary>
    public string? TestLoopbackLogDir => _testLoopbackLogDir;
    /// <summary>Rotation cap for loopback files. Defaults to 1000.</summary>
    public int LoopbackEntriesPerFile => _loopbackEntriesPerFile;
    /// <summary>True (default) → NDJSON files, false → plain text. Drives the file extension.</summary>
    public bool LoopbackUseNdjson => _loopbackUseNdjson;

    // Network sink configs. Record is internal so the sink-serializer registry
    // (see MMP.Herald.Quick.Serializers) can read entries when emitting JSON.
    internal sealed record NetworkSinkConfig(
        string Kind,
        string? Uri,
        string? Host,
        int? Port,
        string? MinLevel,
        IReadOnlyDictionary<string, string>? Headers = null);
    private readonly List<NetworkSinkConfig> _networkSinks = new();

    // -- Collection properties (own their data and CRUD) --
    // NOTE: These Sets are consumed at Build() time. Modifications after Build()
    //       have no effect on the already-built pipeline. Call Build() again to
    //       produce a new pipeline reflecting the latest state.

    /// <summary>Enricher collection. Starts empty by default as of 2026-04-20;
    /// callers opt in to the old system-tag defaults (MachineName, ProcessId, ThreadId)
    /// via <see cref="WithSystemTagsEnrichers"/> or <see cref="EnricherSet.Reset"/>.
    /// Resolved once at Build() time; changes after Build() require a new Build() call.</summary>
    public EnricherSet Enrichers { get; private set; } = null!;
    /// <summary>Event processor collection.
    /// Resolved once at Build() time; changes after Build() require a new Build() call.</summary>
    public EventProcessorSet EventProcessors { get; private set; } = null!;
    /// <summary>User-defined property style collection.
    /// Resolved once at Build() time; changes after Build() require a new Build() call.</summary>
    public PropertyStyleSet PropertyStyles { get; private set; } = null!;
    /// <summary>User-defined category (channel) style collection.
    /// Resolved once at Build() time; changes after Build() require a new Build() call.</summary>
    public CategoryStyleSet CategoryStyles { get; private set; } = null!;
    /// <summary>Custom sink provider collection.
    /// Resolved once at Build() time; changes after Build() require a new Build() call.</summary>
    public SinkProviderSet SinkProviders { get; private set; } = null!;
    /// <summary>Channel sink collection.
    /// Resolved once at Build() time; changes after Build() require a new Build() call.</summary>
    public ChannelSet Channels { get; private set; } = null!;
    /// <summary>Audit sink collection.
    /// Resolved once at Build() time; changes after Build() require a new Build() call.</summary>
    public AuditSinkSet AuditSinks { get; private set; } = null!;
    /// <summary>Pipeline bridge collection.
    /// Resolved once at Build() time; changes after Build() require a new Build() call.</summary>
    public BridgeSet Bridges { get; private set; } = null!;

    /// <summary>Per-sink runtime-override snapshots (run state, tee flags,
    /// per-sink minimum level). The dashboard's per-sink strip writes here
    /// via the management API; the JSON serializer reads from it so a
    /// reboot restores the same operator choices.</summary>
    public SinkRuntimeOverrideSet SinkRuntimeOverrides { get; } = new();

    private string? _registryName;

    // D1 — file-based configuration. When set, Build() uses this
    // JsonLoggingConfig instead of calling BuildJsonConfig() to render
    // the fluent builder state. Populated by the FromConfiguration
    // factories so operators can drop Herald into an existing
    // appsettings.json-driven service without touching the fluent API.
    private Configuration.Json.JsonLoggingConfig? _preloadedConfig;

    private QuickLogBuilder() { }

    private void InitSets()
    {
        Enrichers = new EnricherSet(this);
        EventProcessors = new EventProcessorSet(this);
        PropertyStyles = new PropertyStyleSet(this);
        CategoryStyles = new CategoryStyleSet(this);
        SinkProviders = new SinkProviderSet(this);
        Channels = new ChannelSet(this);
        AuditSinks = new AuditSinkSet(this);
        Bridges = new BridgeSet(this);
    }

    /// <summary>
    /// Create an unnamed builder. The pipeline will not be registered
    /// in the global HeraldRegistry.
    /// </summary>
    public static QuickLogBuilder Create() {
        var builder = new QuickLogBuilder();
        builder.InitSets();
        return builder;
    }

    /// <summary>
    /// Create a named builder. When Build() is called, the pipeline is
    /// automatically registered in HeraldRegistry under this name.
    /// Retrieve it later with HeraldRegistry.Get("name") or
    /// HeraldRegistry.Require("name").
    ///
    /// The name must be unique. If a pipeline with the same name is
    /// already registered, Build() will throw.
    /// </summary>
    public static QuickLogBuilder Create(string name) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var builder = new QuickLogBuilder();
        builder._registryName = name;
        builder.InitSets();
        return builder;
    }

    /// <summary>
    /// Build a pipeline from an on-disk JSON configuration file.
    /// Parses the file, hands the result to the runtime bootstrap, and
    /// returns a builder that Build() / BuildAndCommit() can finish.
    /// Operators use this to drop Herald into services whose logging
    /// config lives in appsettings.json alongside the rest of
    /// Microsoft.Extensions.Configuration.
    /// </summary>
    /// <param name="configFilePath">Absolute or relative path to a
    ///   JSON file matching the <see cref="Configuration.Json.JsonLoggingConfig"/>
    ///   schema.</param>
    /// <param name="name">Optional registry name. Same semantics as the
    ///   <see cref="Create(string)"/> overload — the pipeline is
    ///   registered in <c>HeraldRegistry</c> under this name on Build().</param>
    public static QuickLogBuilder FromConfigurationFile(string configFilePath, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFilePath);
        var json = System.IO.File.ReadAllText(configFilePath);
        return FromConfigurationString(json, name);
    }

    /// <summary>
    /// Build a pipeline from a JSON configuration string. Same shape
    /// as <see cref="FromConfigurationFile"/> but the JSON is supplied
    /// in-memory — useful for tests, cached remote configs, or
    /// configuration generated at runtime from another source.
    /// </summary>
    public static QuickLogBuilder FromConfigurationString(string configJson, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configJson);
        var config = Configuration.LoggingJsonSerializer.Deserialize(configJson);
        return FromConfiguration(config, name);
    }

    /// <summary>
    /// Build a pipeline from an already-parsed
    /// <see cref="Configuration.Json.JsonLoggingConfig"/>. Use this
    /// when you already have the config object (e.g. reconstituted
    /// from a management API or a shared-state store) and don't need
    /// the deserialization hop.
    /// </summary>
    public static QuickLogBuilder FromConfiguration(
        Configuration.Json.JsonLoggingConfig config, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var builder = new QuickLogBuilder();
        builder._preloadedConfig = config;
        builder._registryName = name;
        builder.InitSets();

        // Resolve the JSON-side NamingPolicy id into a concrete policy
        // instance via the registry. This is the cold-start path
        // (FromConfiguration is the entrypoint a host uses on first build);
        // unknown policy id at this point is genuinely a configuration
        // error, not a recoverable hot-reload glitch. Throw loud so the
        // host sees it on startup instead of silently flipping the
        // convention to Pascal. Omitted field (null) flows through as the
        // default — _namingPolicy stays null and the eventual install
        // call uses PropertyNamingPolicy.Pascal.
        if (!string.IsNullOrEmpty(config.NamingPolicy))
        {
            builder._namingPolicy = Templating.NamingPolicyRegistry.Resolve(config.NamingPolicy);
        }
        return builder;
    }

    /// <summary>The registry name, if this builder was created with Create("name").</summary>
    public string? RegistryName => _registryName;

    /// <summary>
    /// Build the pipeline without making it live. Returns a PipelineBuildResult
    /// that can be inspected (ExportConfig) or committed (via QuickLogResult.Commit).
    /// Calls Validate() internally and throws on critical issues.
    /// </summary>
    public PipelineBuildResult Build() {
        ValidateFluentStateOrThrow();

        // Edition validation lives one layer deeper, in
        // DefaultLogPipelineFactory.Create where PipelineEditionValidator.Validate
        // runs against the fully-assembled LogPipelinePolicy. Doing it there
        // catches strategy steps AND event processors AND custom decorators
        // in one composed error, and it covers callers who bypass this
        // QuickLogBuilder facade and go straight to the factory.

        // D1: file-driven config overrides the fluent builder state.
        // When FromConfiguration(...) loaded the config, the preloaded
        // JsonLoggingConfig is authoritative — skip the
        // BuildJsonConfig() render step so operators get exactly what
        // their appsettings.json describes.
        var jsonConfig = _preloadedConfig ?? BuildJsonConfig();
        var configJson = LoggingJsonSerializer.Serialize(jsonConfig);

        var runtimeResult = LoggingRuntimeBootstrap.Bootstrap(
            jsonConfig,
            new ConfiguredLogLevelRegistryFactory(),
            new DefaultLoggingConfigurationMapper());

        var consoleWriter = WrapWithSignalHandlers(
            _consoleWriter ?? new DefaultRichConsoleWriter(), channelSignalHandler: null);
        var hostAdapters = new LoggingHostAdapters(RichConsoleWriter: consoleWriter);

        var additionalProviders = AssembleAdditionalSinkProviders();
        var effectiveTimeProvider = _timeProvider ?? new SystemDateTimeProvider();
        var effectiveEnricher = Enrichers.Resolve();

        var loggingBootstrap = JsonConfiguredLoggingBootstrapFactory.Create(
            dateTimeProvider: effectiveTimeProvider,
            runtimeConfiguration: runtimeResult.RuntimeConfiguration,
            levelRegistry: runtimeResult.LevelRegistry,
            hostAdapters: hostAdapters,
            additionalSinkProviders: additionalProviders,
            eventProcessors: EventProcessors.Items.Count > 0
                ? EventProcessors.Items.ConvertAll(static r => r.Value)
                : null,
            enricher: effectiveEnricher,
            pipelineStrategy: _pipelineStrategy,
            customDecorators: _pipelineDecorators.Count > 0 ? _pipelineDecorators : null,
            destructuringPolicies: _destructuringPolicies.Count > 0 ? _destructuringPolicies : null);

        var accessor = new PipelineAccessor();
        var bootstrapResult = loggingBootstrap.Bootstrap(pipelineAccessor: accessor);
        PopulateAccessor(accessor, bootstrapResult);

        // Install the fast-path redactor on the freshly built StructuredLogger.
        // Done here (not threaded through LogPipelinePolicy) because the redactor
        // is a kernel-dispatch concern, not a pipeline-stage concern — it lives
        // on the logger itself, parallel to the kernel delegate. JSON round-trip
        // and hot-reload integration are deliberate follow-ups (see core-redact
        // branch plan) — for v1 a fresh Build() call is the only entry point.
        if (_fastRedactionRules is { Count: > 0 } rules)
        {
            bootstrapResult.Logger.InstallFastPathRedactor(
                new MMP.Herald.Pipeline.Kernel.FastPathRedactor(rules));
        }

        if (_fastSampleRate is { } sampleRate)
        {
            bootstrapResult.Logger.InstallFastPathSampler(
                new MMP.Herald.Pipeline.Kernel.FastPathSampler(sampleRate));
        }

        if (_fastEnrichmentProperties is { Count: > 0 } enrichProps)
        {
            bootstrapResult.Logger.InstallFastPathEnricher(
                new MMP.Herald.Pipeline.Kernel.FastPathEnricher(enrichProps));
        }

        if (_fastDynamicLevelSwitch is { } dynSwitch)
        {
            bootstrapResult.Logger.InstallFastPathDynamicLevel(
                new MMP.Herald.Pipeline.Kernel.FastPathDynamicLevel(
                    dynSwitch, _fastDynamicLevelCategoryMap, runtimeResult.LevelRegistry));
        }

        // Fast-path async sink: wrap the routed-sinks composite so the
        // kernel hands buffers to a single FastPathAsyncSink instead of
        // fanning them out synchronously. The wrapper materialises one
        // LogEvent and enqueues; the background consumer fans out to
        // the original composite off-thread.
        //
        // Limitation (Option A topology — see future-direction.md): one
        // channel + one consumer thread for the whole sink set means a
        // slow inner sink can stall the rest. The per-sink wrapper
        // variant lives as a future-direction follow-up.
        if (_fastAsyncSinkCapacity is { } asyncCapacity)
        {
            InstallFastPathAsyncSinkWrapper(bootstrapResult.Logger, accessor, asyncCapacity);
        }

        // First-dispatch announcement suppression — must land BEFORE the
        // InstallNamingPolicy call below. InstallNamingPolicy is now the
        // announcement-fire site for builder-built loggers (the multi-policy
        // interceptor takes the dispatch hot path away from TryGetCachedNames,
        // so a consumer who lives entirely on intercepted call sites would
        // otherwise never trigger the announcement). Build() runs synchronously
        // before any consumer holds a Logger reference, so calling Suppress
        // here is safely ordered ahead of EnsureAnnouncementFired.
        if (_suppressNamingPolicyAnnouncement)
        {
            bootstrapResult.Logger.SuppressAnnouncement();
        }

        // Property-naming policy install. Threaded through here (rather than
        // the constructor) so the JSON-round-trip path can resolve the policy
        // id via NamingPolicyRegistry inside Build() and hand the resulting
        // instance to the same install method. Default is PascalCasePolicy —
        // the spec's 1.0+ baseline — when no explicit policy is configured.
        // Fires the announcement gate at the end via EnsureAnnouncementFired;
        // the suppression flag (above) is already set on the logger by the
        // time the gate consults it.
        bootstrapResult.Logger.InstallNamingPolicy(
            _namingPolicy ?? MMP.Herald.Templating.PropertyNamingPolicy.Pascal);

        return new PipelineBuildResult(
            bootstrapResult.Logger, bootstrapResult, effectiveTimeProvider, accessor, configJson, _registryName);
    }

    // Wraps the existing routed-sinks composite in a FastPathAsyncSink,
    // recompiles a single-child kernel, installs both the wrapper and
    // the new kernel atomically. Used both by initial Build (above) and
    // the hot-reload installer (which re-applies the same shape after a
    // pipeline rebuild).
    internal static void InstallFastPathAsyncSinkWrapper(
        Pipeline.StructuredLogger logger,
        Pipeline.PipelineAccessor accessor,
        int boundedCapacity)
    {

        // The pipeline factory registered the routed-sinks composite with
        // the accessor at build time (see DefaultLogSinkRouterFactory).
        // We need that reference to wire the async wrapper around it.
        var composite = accessor.Get<Pipeline.SafeCompositeLogger>();
        if (composite is null)
        {
            // No composite registered — happens with a non-default sink
            // router or a pipeline built without sinks. Skip silently;
            // the async wrapper is purely additive.
            return;
        }

        var asyncWrapper = new Pipeline.Kernel.FastPathAsyncSink(composite, boundedCapacity);
        var newKernel = Pipeline.Kernel.KernelCompiler.CompileFanOut(
            new MMP.Herald.ILogger[] { asyncWrapper });

        // Install the wrapper first so a concurrent reload observing the
        // logger sees a consistent (kernel + wrapper) pair after this
        // call returns. Drain the prior wrapper if Install returned one.
        var prior = logger.InstallFastPathAsyncSink(asyncWrapper);
        logger.SwapKernel(newKernel);

        if (prior is not null)
        {
            // Drain the prior wrapper off-thread so we don't block the
            // caller. Drain is best-effort — if the prior inner is hung
            // the cancellation path inside DisposeAsync trips after the
            // configured timeout.
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try { await prior.DisposeAsync().ConfigureAwait(false); }
                catch { /* best-effort retire */ }
            });
        }
    }

    // D1: when a preloaded JSON config is the source of truth, skip the
    // fluent-state validator. Validate() looks at fluent state only; it
    // cannot see sinks declared in the preloaded config and would reject a
    // file-driven build as "no sinks configured." The runtime bootstrap
    // still surfaces config-level issues.
    private void ValidateFluentStateOrThrow()
    {
        if (_preloadedConfig is not null) return;

        var validation = Validate();
        if (!validation.HasCritical) return;

        var messages = string.Join("; ", validation.Issues
            .Where(i => i.Severity == ValidationSeverity.Critical)
            .Select(i => i.Message));
        throw new InvalidOperationException($"Builder validation failed: {messages}");
    }

    // Assemble the additional-providers list in registration order:
    // channels first, then audit sinks, then user-registered providers,
    // then the optional webhook-with-rules override, then bridges. Each
    // group has an inline justification for why it exists; the grouping
    // reduces Build()'s nesting and keeps the per-group loop bodies short.
    private List<ILogSinkProvider> AssembleAdditionalSinkProviders()
    {
        var providers = new List<ILogSinkProvider>();

        foreach (var channel in Channels.Items)
        {
            var effectiveWriter = WrapWithSignalHandlers(
                channel.Writer, channel.SignalHandler);
            providers.Add(new ChannelSinkProvider(channel.Name, effectiveWriter, channel.Transformer));
        }

        for (var i = 0; i < AuditSinks.Items.Count; i++)
        {
            var audit = AuditSinks.Items[i];
            var auditWriter = WrapWithSignalHandlers(audit.Writer, audit.SignalHandler);
            providers.Add(new ChannelSinkProvider($"audit_{i}", auditWriter, audit.Transformer));
        }

        foreach (var reg in SinkProviders.Items)
        {
            providers.Add(reg.Value);
        }

        for (var i = 0; i < Bridges.Items.Count; i++)
        {
            providers.Add(new BridgeSinkProvider(Bridges.Items[i], BridgeSinkKind(i)));
        }

        return providers;
    }

    private static void PopulateAccessor(PipelineAccessor accessor, LoggingBootstrapResult bootstrapResult)
    {
        if (bootstrapResult.DynamicLevelPolicy is not null)
            accessor.Register(bootstrapResult.DynamicLevelPolicy);
        if (bootstrapResult.MetricsRegistry is not null)
            accessor.Register(bootstrapResult.MetricsRegistry);
        if (bootstrapResult.HotReloadBootstrap is not null)
            accessor.Register(bootstrapResult.HotReloadBootstrap);
    }

    /// <summary>
    /// Build AND make it live in one call. Convenience for the common case
    /// where you don't need to inspect before committing.
    /// Equivalent to: var build = Build(); return QuickLogResult.FromBuild(build);
    /// </summary>
    public QuickLogResult BuildAndCommit() {
        var buildResult = Build();
        return QuickLogResult.FromBuild(buildResult);
    }

    /// <summary>
    /// Serialize the current builder state to a JSON string WITHOUT
    /// running the runtime bootstrap. Equivalent to <c>ExportConfig()</c>
    /// but skips <c>Build()</c> — no level registry construction, no
    /// sink instantiation, no router factory work.
    ///
    /// <para>The point: per-sink runtime PATCHes (run state, tee
    /// flags, sink-level minimum) only need to write the JSON; they
    /// do not need a fresh pipeline. The full <c>Build()</c> path was
    /// adding seconds of latency to a click that should land
    /// instantly. Use this when you only need the on-disk shape.</para>
    ///
    /// <para>Callers that need a built / committed pipeline keep
    /// using <c>Build()</c> or <c>BuildAndCommit()</c>; this method
    /// is purely a serialization shortcut.</para>
    /// </summary>
    public string ExportConfigJson()
    {
        ValidateFluentStateOrThrow();
        var jsonConfig = _preloadedConfig ?? BuildJsonConfig();
        return LoggingJsonSerializer.Serialize(jsonConfig);
    }

    /// <summary>
    /// Write the current builder state directly to disk WITHOUT a
    /// runtime bootstrap. The cheap counterpart of
    /// <see cref="ExportConfigToFile"/> — same on-disk shape, none
    /// of the pipeline-rebuild cost.
    /// </summary>
    public void ExportConfigJsonToFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var directory = System.IO.Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllText(filePath, ExportConfigJson());
    }

    // -- Private helpers --

    private JsonLoggingConfig BuildJsonConfig() {
        var levelStyles = BuildLevelStyles();
        var propertyStyles = BuildDefaultPropertyStyles();
        var (sinks, routes) = BuildSinksAndRoutes();

        var canonicalBaseLevels = new List<JsonLogLevelDefinition>
        {
            new(Services.LogLevelKeys.Verbose, "TRC"),
            new(Services.LogLevelKeys.Debug, "DBG"),
            new(Services.LogLevelKeys.Information, "INF"),
            new(Services.LogLevelKeys.Warning, "WRN"),
            new(Services.LogLevelKeys.Error, "ERR"),
            new(Services.LogLevelKeys.Fatal, "FTL")
        };
        var baseLevels = ApplyLevelOrder(canonicalBaseLevels, _customLevels, _levelOrder);

        var aliases = new List<JsonLogOutputAliasConfig>
        {
            new("default", "console")
        };

        // Hot reload is a user opt-in via WithHotReload. The Swappable step
        // in a strategy enables swap *capability* (SwappableLogger in the
        // chain, useful for tests and manual swaps), but it does not turn on
        // the file watcher / debounce / auto-reload machinery that
        // HotReloadableLoggingBootstrap wires up. That machinery needs the
        // user to ask for it explicitly. Decoupling also lets kernel
        // eligibility stay clean: hot reload genuinely requires chain path
        // today, while Swappable alone does not.
        var effectiveStrategy = _pipelineStrategy ?? Configuration.PipelineStrategy.Default();
        var effectiveHotReload = _hotReloadEnabled;

        // Build pipeline step entries with config properties for each step
        var pipelineStepConfigs = new List<JsonPipelineStepConfig>();
        foreach (var step in effectiveStrategy.Steps)
        {
            pipelineStepConfigs.Add(new JsonPipelineStepConfig(
                StepName: step.Name,
                Alias: GetAlias(step.Name),
                Vendor: step.Vendor.Name,
                Version: HeraldVersion.Version,
                Config: BuildStepConfig(step.Name)));
        }

        // Build channel configs
        var channelConfigs = new List<JsonChannelConfig>();
        foreach (var ch in Channels.Items)
            channelConfigs.Add(new JsonChannelConfig(ch.Name));

        // Build enricher list. Each enricher serializes itself via the
        // virtual ToJsonConfig() so its constructor parameters round-trip
        // through JSON. Stateless enrichers fall back to the default
        // implementation, which emits Kind only.
        var enricherConfigs = new List<JsonEnricherConfig>();
        foreach (var e in Enrichers.Items)
            enricherConfigs.Add(e.Value.ToJsonConfig());

        // Build event processor list
        var processorNames = new List<string>();
        foreach (var p in EventProcessors.Items)
            processorNames.Add(p.Name);

        // Build custom-pipeline-decorator list. Each decorator serializes
        // itself via the virtual ToJsonConfig() default, which pairs the
        // step name with GetConfiguration() so a Reload reconstructs the
        // same decorator with the same applied config. Decorators that
        // hold state outside the schema-declared fields should override
        // ToJsonConfig() to capture it.
        var decoratorConfigs = new List<JsonPipelineDecoratorConfig>();
        foreach (var d in _pipelineDecorators)
            decoratorConfigs.Add(d.ToJsonConfig());

        // Custom levels normally land in additionalLevels and need a
        // placement to register. When _levelOrder is set, the custom
        // levels were already merged into baseLevels above, so the
        // additionalLevels list stays empty for that path.
        var additionalLevels = _levelOrder is null
            ? _customLevels?.ConvertAll(l => new JsonLogLevelDefinition(l.Key, l.DisplayName)) ?? []
            : new List<JsonLogLevelDefinition>();
        return new JsonLoggingConfig(
            Levels: new JsonLogLevelsConfig(baseLevels,
                additionalLevels,
                []),
            MinimumLevel: _minimumLevel,
            Async: new JsonAsyncLogPolicyConfig(
                Enabled: _asyncEnabled,
                Capacity: _asyncCapacity,
                DropStrategy: _asyncDropStrategy,
                DeferRendering: _asyncDeferRendering),
            Batching: _batchingEnabled ? new JsonBatchingPolicyConfig(
                Enabled: true,
                MaxBatchSize: _batchMaxSize,
                MaxBatchDelayMs: _batchDelayMs) : null,
            DumpRegisteredLevelsToConsole: _dumpLevels,
            Aliases: aliases,
            LevelStyles: levelStyles,
            Sinks: sinks,
            Routes: routes,
            PropertyStyles: propertyStyles,
            IncludeCallerInfo: _includeCallerInfo,
            IncludeActivityContext: _includeActivityContext,
            HotReload: effectiveHotReload ? new JsonHotReloadConfig(Enabled: true, DebounceMs: _hotReloadDebounceMs) : null,
            DynamicLevels: _dynamicLevelsEnabled ? BuildDynamicLevelConfig() : null,
            Sampling: _samplingRules is { Count: > 0 } ? new JsonSamplingConfig(Enabled: true, Rules: _samplingRules) : null,
            // Optional pipeline-step configs that round-trip through JSON.
            // Both stay null when disabled so existing fixtures comparing
            // against the JSON shape don't see new fields appear.
            PostFiltering: _postFilteringEnabled
                && _postFilteringTrigger is not null
                && _postFilteringNormal is not null
                && _postFilteringEscalated is not null
                ? new JsonPostFilteringConfig(
                    Enabled: true,
                    MaxBatchSize: _postFilteringMaxBatchSize,
                    MaxBatchDelayMs: _postFilteringMaxBatchDelayMs,
                    TriggerCondition: _postFilteringTrigger,
                    NormalFilter: _postFilteringNormal,
                    EscalatedFilter: _postFilteringEscalated)
                : null,
            FlightRecorder: _flightRecorderEnabled
                ? new JsonFlightRecorderConfig(
                    Enabled: true,
                    BufferSize: _flightRecorderBufferSize,
                    MinimumLevel: _flightRecorderMinLevel,
                    TriggerLevel: _flightRecorderTriggerLevel)
                : null,
            PipelineSteps: pipelineStepConfigs,
            // Strategy *selection* survives a Reload only if we record it
            // alongside the steps array. ResolveName() returns the matching
            // preset name when the ordered steps line up with one of the
            // built-ins, otherwise "custom" — and "custom" falls back to
            // PipelineStrategy.FromNames(steps) on the read side.
            PipelineStrategyName: effectiveStrategy.ResolveName(),
            PipelineName: _registryName,
            Channels: channelConfigs.Count > 0 ? channelConfigs : null,
            Enrichers: enricherConfigs.Count > 0 ? enricherConfigs : null,
            PipelineDecorators: decoratorConfigs.Count > 0 ? decoratorConfigs : null,
            EventProcessors: processorNames.Count > 0 ? processorNames : null,
            BridgeCount: Bridges.Items.Count > 0 ? Bridges.Items.Count : null,
            AuditSinkCount: AuditSinks.Items.Count > 0 ? AuditSinks.Items.Count : null,
            CustomSinkProviders: SinkProviders.Items.Count > 0
                ? SinkProviders.Items.ConvertAll(static r => r.Name) : null,
            FileRolling: _logFileRolling,
            CategoryStyles: CategoryStyles.Items.Count > 0 ? CategoryStyles.Items : null,
            TestLoopbackUrl: _testLoopbackUrl,
            TestLoopbackLogDir: _testLoopbackLogDir,
            LoopbackEntriesPerFile: _loopbackEntriesPerFile,
            LoopbackUseNdjson: _loopbackUseNdjson,
            // Fast-path companions: emit the JSON-shaped slot for each
            // one that is configured. The reader side
            // (HotReloadableLoggingBootstrap.ExecuteReload) reconstructs
            // them and re-installs on the StructuredLogger. Initial
            // BuildAndCommit() installs from fluent state directly; this
            // serialisation is what makes those companions survive a
            // subsequent Reload(json).
            FastPathRedaction: BuildFastPathRedactionConfig(),
            FastPathSampling: _fastSampleRate is { } fsr
                ? new JsonFastPathSamplingConfig(fsr)
                : null,
            FastPathEnrichment: BuildFastPathEnrichmentConfig(),
            FastPathDynamicLevel: _fastDynamicLevelSwitch is { } fds
                ? new JsonFastPathDynamicLevelConfig(
                    fds.MinimumLevel.Key,
                    BuildFastPathCategoryMapSnapshot())
                : null,
            FastPathAsyncSink: _fastAsyncSinkCapacity is { } asyncCap
                ? new JsonFastPathAsyncSinkConfig(asyncCap)
                : null,
            // Property-naming policy id round-trips as a string. Null on the
            // builder serialises as null in the JSON, which the reader
            // resolves to PascalCasePolicy (the spec default). When a custom
            // policy is configured we write its Id so a Reload can recover
            // the same instance via NamingPolicyRegistry.Resolve.
            NamingPolicy: _namingPolicy?.Id);
    }

    private JsonFastPathRedactionConfig? BuildFastPathRedactionConfig()
    {
        if (_fastRedactionRules is not { Count: > 0 } rules) return null;
        var entries = new List<JsonFastPathRedactionRule>(rules.Count);
        foreach (var r in rules)
        {
            // Only exact-name + simple-mode rules survive into the fast-path
            // section. Pattern / event-action / value-pattern rules cannot
            // be installed via WithFastRedaction at runtime, so emitting
            // them here would round-trip into a constructor that throws.
            // The fluent API's WithFastRedaction itself would have rejected
            // those at build time, so in practice this filter is defensive.
            if (r.PatternKind != Pipeline.Processors.RedactionPatternKind.ExactName) continue;
            if (r.EventAction != Pipeline.Processors.RedactionEventAction.None) continue;
            if (r.ValuePattern is not null) continue;
            if (r.When is not null) continue;
            entries.Add(new JsonFastPathRedactionRule(
                PropertyName: r.PropertyNamePattern,
                Mode: r.Mode.Value.ToLowerInvariant(),
                MaskChar: r.MaskChar,
                VisibleChars: r.VisibleChars));
        }
        return entries.Count > 0 ? new JsonFastPathRedactionConfig(entries) : null;
    }

    // Snapshot the per-category override map at JSON build time. Returns
    // null when no map is wired or the map is empty so the JSON omits the
    // section entirely on global-only configurations. Each entry is a
    // category name → level key pair; the reload installer re-applies
    // them via CategoryLevelSwitchMap.SetCategoryLevel.
    private System.Collections.Generic.IReadOnlyDictionary<string, string>? BuildFastPathCategoryMapSnapshot()
    {
        if (_fastDynamicLevelCategoryMap is null) return null;
        var overrides = _fastDynamicLevelCategoryMap.GetAllOverrides();
        if (overrides.Count == 0) return null;
        var snap = new Dictionary<string, string>(overrides.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in overrides)
        {
            snap[kv.Key] = kv.Value.MinimumLevel.Key;
        }
        return snap;
    }

    private JsonFastPathEnrichmentConfig? BuildFastPathEnrichmentConfig()
    {
        if (_fastEnrichmentProperties is not { Count: > 0 } props) return null;
        var entries = new List<JsonFastPathEnrichmentEntry>(props.Count);
        foreach (var p in props)
        {
            // Only string-valued static properties survive into JSON. The
            // JSON shape doesn't model arbitrary CLR objects; any caller
            // who wants a non-string value should serialise it themselves
            // before WithFastEnrichment, or stay on the legacy enricher
            // path which can route through the EnricherJsonRegistry.
            entries.Add(new JsonFastPathEnrichmentEntry(
                Name: p.Name,
                Value: p.ResolvedValue?.ToString() ?? string.Empty));
        }
        return new JsonFastPathEnrichmentConfig(entries);
    }

    /// <summary>
    /// Build configuration properties for a specific pipeline step.
    /// Delegates to the step-serializer registry when an entry exists;
    /// otherwise falls back to the component schema defaults so plugin
    /// steps without a bespoke serializer still render in the dashboard.
    /// Returns null if the step has no configurable properties.
    /// </summary>
    private Dictionary<string, object?>? BuildStepConfig(string stepName)
    {
        var registered = Serializers.QuickLogBuilderSerializers.GetStep(stepName);
        if (registered is not null)
        {
            var built = registered.BuildConfig(this);
            return built is { Count: > 0 } ? built : null;
        }

        var schema = Pipeline.ComponentSchemaRegistry.GetSchema(stepName);
        if (schema is null || schema.Count == 0)
            return null;

        var config = new Dictionary<string, object?>(schema.Count);
        foreach (var field in schema)
            config[field.Name] = field.DefaultValue;
        return config;
    }

    /// <summary>
    /// Apply the user's level-order override on top of the canonical
    /// baseLevels and any custom levels. Returns a fresh list in the
    /// order the user supplied. Keys not recognised are skipped (the
    /// management API validates against the registry before calling
    /// here, so an unknown key is most likely a stale dashboard
    /// snapshot — silently skipping protects the pipeline). Levels
    /// the user didn't list at all are appended in canonical order so
    /// nothing is dropped.
    /// </summary>
    private static List<JsonLogLevelDefinition> ApplyLevelOrder(
        List<JsonLogLevelDefinition> canonicalBase,
        List<Levels.LogLevel>? customLevels,
        List<string>? requestedOrder)
    {
        if (requestedOrder is null || requestedOrder.Count == 0)
            return canonicalBase;

        var pool = new Dictionary<string, JsonLogLevelDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var lvl in canonicalBase) pool[lvl.Key] = lvl;
        if (customLevels is not null)
        {
            foreach (var c in customLevels)
                pool[c.Key] = new JsonLogLevelDefinition(c.Key, c.DisplayName);
        }

        var ordered = new List<JsonLogLevelDefinition>(pool.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in requestedOrder)
        {
            if (key is null) continue;
            if (!pool.TryGetValue(key, out var def)) continue;     // unknown key — skip
            if (!seen.Add(def.Key)) continue;                       // duplicate in request
            ordered.Add(def);
        }
        // Append anything the request didn't mention so we don't drop
        // a level the runtime knows about. Canonical order first, then
        // customs (alphabetical for stability).
        foreach (var lvl in canonicalBase)
            if (seen.Add(lvl.Key)) ordered.Add(lvl);
        if (customLevels is not null)
        {
            foreach (var c in customLevels.OrderBy(l => l.Key, StringComparer.OrdinalIgnoreCase))
                if (seen.Add(c.Key)) ordered.Add(new JsonLogLevelDefinition(c.Key, c.DisplayName));
        }
        return ordered;
    }

    private JsonDynamicLevelConfig? BuildDynamicLevelConfig()
    {
        if (!_dynamicLevelsEnabled) return null;
        List<JsonCategoryLevelOverride>? overrides = null;
        if (_categoryLevelOverrides is not null && _categoryLevelOverrides.Count > 0)
        {
            overrides = new List<JsonCategoryLevelOverride>();
            foreach (var kvp in _categoryLevelOverrides)
                overrides.Add(new JsonCategoryLevelOverride(kvp.Key, kvp.Value));
        }
        return new JsonDynamicLevelConfig(Enabled: true, CategoryOverrides: overrides);
    }

    private List<JsonLogLevelStyleConfig> BuildLevelStyles()
    {
        // Defaults designed for dark-theme dashboards and terminals:
        // - Warning: Black text on Yellow background (high-visibility alert)
        // - Error: Yellow text on Red background (critical contrast)
        // - Fatal: DarkRed + bold + italic (maximum urgency)
        var defaults = new List<JsonLogLevelStyleConfig>
        {
            new(Services.LogLevelKeys.Verbose, Services.KnownAnsiColors.DimGray),
            new(Services.LogLevelKeys.Debug, Services.KnownAnsiColors.Gray),
            new(Services.LogLevelKeys.Information, Services.KnownAnsiColors.Green),
            new(Services.LogLevelKeys.Warning, Services.KnownAnsiColors.Black, UseBold: true, BackgroundColorName: Services.KnownAnsiColors.Yellow),
            new(Services.LogLevelKeys.Error, Services.KnownAnsiColors.Black, UseBold: true, BackgroundColorName: Services.KnownAnsiColors.Red),
            new(Services.LogLevelKeys.Fatal, Services.KnownAnsiColors.DarkRed, UseBold: true, UseItalic: true)
        };

        if (_levelStyleOverrides is null || _levelStyleOverrides.Count == 0)
            return defaults;

        // Merge: overrides replace defaults by level key
        var merged = new Dictionary<string, JsonLogLevelStyleConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in defaults) merged[d.LevelKey] = d;
        foreach (var o in _levelStyleOverrides) merged[o.LevelKey] = o;
        return [.. merged.Values];
    }

    private List<JsonPropertyStyleConfig> BuildDefaultPropertyStyles() {
        var styles = new List<JsonPropertyStyleConfig>
        {
            // Identity
            new(Services.KnownPropertyNames.UserId, Services.KnownAnsiColors.Cyan, UseBold: true),
            new(Services.KnownPropertyNames.UserName, Services.KnownAnsiColors.Cyan, UseBold: true),
            new(Services.KnownPropertyNames.EntityId, Services.KnownAnsiColors.Cyan, UseBold: true),
            new(Services.KnownPropertyNames.EntityName, Services.KnownAnsiColors.Cyan, UseBold: true),

            // Actions and operations
            new(Services.KnownPropertyNames.Action, Services.KnownAnsiColors.Yellow, UseBold: true),
            new(Services.KnownPropertyNames.Operation, Services.KnownAnsiColors.Yellow, UseBold: true),
            new(Services.KnownPropertyNames.Status, Services.KnownAnsiColors.Green),
            new(Services.KnownPropertyNames.Result, Services.KnownAnsiColors.Green),

            // Values and changes
            new(Services.KnownPropertyNames.Value, Services.KnownAnsiColors.White, UseBold: true),
            new(Services.KnownPropertyNames.Delta, Services.KnownAnsiColors.Yellow),
            new(Services.KnownPropertyNames.Count, Services.KnownAnsiColors.White, UseBold: true),
            new(Services.KnownPropertyNames.Amount, Services.KnownAnsiColors.Gold),
            new(Services.KnownPropertyNames.Duration, Services.KnownAnsiColors.DimGray),
            new(Services.KnownPropertyNames.Elapsed, Services.KnownAnsiColors.DimGray),

            // Location and context
            new(Services.KnownPropertyNames.Path, Services.KnownAnsiColors.SkyBlue, UseItalic: true),
            new(Services.KnownPropertyNames.Source, Services.KnownAnsiColors.SkyBlue),
            new(Services.KnownPropertyNames.Target, Services.KnownAnsiColors.SkyBlue, UseItalic: true),
            new(Services.KnownPropertyNames.Endpoint, Services.KnownAnsiColors.SkyBlue),

            // Error and diagnostic
            new(Services.KnownPropertyNames.Error, Services.KnownAnsiColors.Red, UseBold: true),
            new(Services.KnownPropertyNames.Reason, Services.KnownAnsiColors.Red),
            new(Services.KnownPropertyNames.Exception, Services.KnownAnsiColors.Red, UseBold: true),
            new(Services.KnownPropertyNames.StackTrace, Services.KnownAnsiColors.DimGray, UseItalic: true),

            // Classification
            new(Services.KnownPropertyNames.Category, Services.KnownAnsiColors.Blue),
            new(Services.KnownPropertyNames.Type, Services.KnownAnsiColors.Blue),
            new(Services.KnownPropertyNames.Kind, Services.KnownAnsiColors.Teal),
            new(Services.KnownPropertyNames.Tag, Services.KnownAnsiColors.Magenta),

            // Timing
            new(Services.KnownPropertyNames.Timestamp, Services.KnownAnsiColors.DimGray, UseItalic: true),
            new(Services.KnownPropertyNames.TimeOfDay, Services.KnownAnsiColors.DimGray, UseItalic: true)
        };

        styles.AddRange(PropertyStyles.Items);
        return styles;
    }

    private (List<JsonLogSinkConfig> Sinks, List<JsonLogRouteConfig> Routes) BuildSinksAndRoutes() {
        var sinks = new List<JsonLogSinkConfig>();
        var routes = new List<JsonLogRouteConfig>();

        // All sink kinds contribute through the serializer registry. Each
        // built-in sink owns its own mapping (see Quick/Serializers/Sinks/*),
        // and custom sinks extend the registry at runtime.
        var sinkContext = new Serializers.SinkSerializerContext(ComputeDefaultRoutePredicate());
        foreach (var serializer in Serializers.QuickLogBuilderSerializers.Sinks)
        {
            foreach (var (sink, route) in serializer.BuildSinkRoutes(this, sinkContext))
            {
                sinks.Add(ApplyLabelOverride(sink));
                routes.Add(route);
            }
        }

        return (sinks, routes);
    }

    // Apply any per-sink label override the operator set via WithSinkLabel.
    // The gate's auto-generator handles the empty-string case at hoist; we
    // just propagate whatever the operator configured. Sinks not in the map
    // pass through with their original Label (typically null → auto-generate).
    private JsonLogSinkConfig ApplyLabelOverride(JsonLogSinkConfig sink) =>
        _sinkLabels.TryGetValue(sink.Name, out var label) && label != sink.Label
            ? sink with { Label = label }
            : sink;

    private IRenderedLogOutputWriter WrapWithSignalHandlers(
        IRenderedLogOutputWriter writer,
        ILogSignalHandler? channelSignalHandler) {
        ILogSignalHandler? effectiveHandler = (channelSignalHandler, _globalSignalHandler) switch
        {
            (not null, not null) => new CompositeLogSignalHandler([channelSignalHandler, _globalSignalHandler]),
            (not null, null) => channelSignalHandler,
            (null, not null) => _globalSignalHandler,
            _ => null
        };

        return effectiveHandler is not null
            ? new SignalDispatchingWriter(writer, effectiveHandler)
            : writer;
    }

    private static string BridgeSinkKind(int index) =>
        $"{Services.KnownSinkKinds.PipelineBridge}_{index}";

}
