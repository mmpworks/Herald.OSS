#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Events;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Output.Rich;
using MMP.Herald.Pipeline;
using MMP.Herald.Pipeline.Processors;
using MMP.Herald.Predicates;
using MMP.Herald.Routing;
using MMP.Herald.Signals;
using MMP.Herald.Templating;
using MMP.Herald.Time;

namespace MMP.Herald.Quick;

// With* fluent configuration methods.
// Collection operations delegate to their respective Set classes.
// These are convenience shortcuts — callers can also use .Enrichers, .Channels, etc. directly.
public sealed partial class QuickLogBuilder
{
    // -- Property naming policy --

    /// <summary>
    /// Set the property-naming policy applied by the typed-args runtime
    /// dispatch path. The Herald.OSS 1.0+ default is
    /// <see cref="PropertyNamingPolicy.Pascal"/> — template tokens drive
    /// property names, matching Serilog / MEL / NLog convention. Opt in to
    /// <see cref="PropertyNamingPolicy.Camel"/> to preserve pre-1.0 Herald
    /// behaviour (caller-argument-expression wins), or
    /// <see cref="PropertyNamingPolicy.Snake"/> for OpenTelemetry-friendly
    /// snake_case output.
    ///
    /// <para>
    /// Custom policies (e.g. a future <c>KebabCasePolicy</c>) work too —
    /// register them with <c>NamingPolicyRegistry.Register</c> before
    /// any <c>Reload(json)</c> call that names them by id.
    /// </para>
    /// </summary>
    /// <param name="policy">The naming policy to apply. Use the static
    /// accessors on <see cref="PropertyNamingPolicy"/> for built-ins.</param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="policy"/> is null.</exception>
    public QuickLogBuilder WithNamingPolicy(IPropertyNamingPolicy policy)
    {
        if (policy is null) throw new ArgumentNullException(nameof(policy));
        _namingPolicy = policy;
        return this;
    }

    /// <summary>
    /// Read the currently-configured naming policy. Returns
    /// <see cref="PropertyNamingPolicy.Pascal"/> when no explicit policy
    /// has been set — that's the default the eventual <c>StructuredLogger</c>
    /// will receive.
    /// </summary>
    public IPropertyNamingPolicy GetNamingPolicy()
    {
        return _namingPolicy ?? PropertyNamingPolicy.Pascal;
    }

    /// <summary>
    /// Carry-forward hook used by <c>QuickLogResult.RebuildFrom</c>: writes
    /// the supplied policy onto the builder only when the builder has not
    /// explicitly chosen one. Lets a rebuild preserve the live pipeline's
    /// policy when the caller's builder doesn't override it, while still
    /// honouring an explicit <see cref="WithNamingPolicy"/> on that
    /// builder. Spec invariant: silent default-flip on hot-reload is the
    /// pattern the project has scars from (see
    /// <c>feedback_hot_reload_gate_preservation</c>).
    /// </summary>
    internal void SetNamingPolicyIfUnset(IPropertyNamingPolicy fallback)
    {
        if (fallback is null) throw new ArgumentNullException(nameof(fallback));
        _namingPolicy ??= fallback;
    }

    /// <summary>
    /// Suppress the one-shot "Active naming policy: ..." Info event that
    /// Herald emits on first dispatch (Phase 5 / v1.0). The announcement
    /// is per-<see cref="MMP.Herald.Pipeline.StructuredLogger"/> instance
    /// and fires through the logger's own sinks, so it lands wherever
    /// regular events do — call this if your service treats Info as
    /// load-bearing structured output and the announcement noise would
    /// pollute it.
    ///
    /// <para>
    /// Equivalent process-wide alternative: set the environment variable
    /// <c>HERALD_NAMINGPOLICY_QUIET=1</c> before the host starts.
    /// </para>
    /// </summary>
    public QuickLogBuilder SuppressNamingPolicyAnnouncement()
    {
        _suppressNamingPolicyAnnouncement = true;
        return this;
    }

    /// <summary>
    /// Internal accessor read by <c>QuickLogBuilder.Build()</c> when it
    /// threads suppression into the freshly built
    /// <see cref="MMP.Herald.Pipeline.StructuredLogger"/>.
    /// </summary>
    internal bool IsNamingPolicyAnnouncementSuppressed => _suppressNamingPolicyAnnouncement;

    // -- Console sink --

    /// <summary>
    /// Add a standard console sink for regular log output.
    /// Channel events are excluded from this sink.
    /// </summary>
    public QuickLogBuilder WithConsoleSink(IRenderedLogOutputWriter? writer = null, string? minLevel = null) {
        _includeConsole = true;
        _consoleWriter = writer;
        _consoleMinLevel = minLevel;
        return this;
    }

    /// <summary>
    /// Add a sink that silently drops every event. Intended for benchmarks
    /// that measure pipeline cost in isolation and for configurations that
    /// want the pipeline present without downstream I/O.
    /// </summary>
    public QuickLogBuilder WithNullSink(string? minLevel = null) {
        _includeNullSink = true;
        _nullSinkMinLevel = minLevel;
        return this;
    }

    // -- File sink --

    // ── Loopback knobs (pipeline-level) ──────────────────────────
    // These four setters configure the loopback channels every sink
    // in the pipeline shares. Per-sink opt-in (RunState + TeeLiveTo*)
    // happens elsewhere; these set the shared destinations.

    /// <summary>
    /// Set the pipeline's loopback URL. The URL leg of every sink in
    /// run-state Test (or Live with TeeLiveToUrl on) POSTs each event
    /// to this URL as one NDJSON line. Supports two placeholders that
    /// the router factory substitutes once at wrap time:
    /// <c>{pipelineName}</c> → this pipeline's registry name,
    /// <c>{sinkName}</c>     → the per-sink Name field.
    /// Pass <c>null</c> or empty to disable the URL leg pipeline-wide.
    /// </summary>
    public QuickLogBuilder WithTestLoopbackUrl(string? url) {
        _testLoopbackUrl = string.IsNullOrWhiteSpace(url) ? null : url;
        return this;
    }

    /// <summary>
    /// Set the pipeline's loopback log directory. Sinks teeing to file
    /// write <c>{logDir}/{TestOutFileSuffix}-{SinkName}.{ndjson|log}</c>,
    /// rolling every <see cref="WithLoopbackEntriesPerFile"/> events.
    /// Pass <c>null</c> or empty to disable the file leg pipeline-wide.
    /// </summary>
    public QuickLogBuilder WithTestLoopbackLogDir(string? logDir) {
        _testLoopbackLogDir = string.IsNullOrWhiteSpace(logDir) ? null : logDir;
        return this;
    }

    /// <summary>
    /// Set the rotation cap for loopback files. The writer rolls to a
    /// new file each time it crosses this count. Defaults to 1000.
    /// Values &lt;= 0 are clamped to 1000 so a misconfigured input
    /// cannot turn the rolling logic into a tight loop.
    /// </summary>
    public QuickLogBuilder WithLoopbackEntriesPerFile(int entriesPerFile) {
        _loopbackEntriesPerFile = entriesPerFile > 0 ? entriesPerFile : 1000;
        return this;
    }

    /// <summary>
    /// Choose the loopback file format. <c>true</c> (default) writes
    /// NDJSON — one structured event per line, machine-parseable, the
    /// same shape the URL leg uses. <c>false</c> writes plain text
    /// rendered through the message field. The file extension follows
    /// the choice (<c>.ndjson</c> vs <c>.log</c>) so a directory listing
    /// tells the operator the format at a glance.
    /// </summary>
    public QuickLogBuilder WithLoopbackUseNdjson(bool useNdjson) {
        _loopbackUseNdjson = useNdjson;
        return this;
    }

    /// <summary>Add a file sink. Kind is inferred from extension (.ndjson/.jsonl → json_file, else text_file).</summary>
    public QuickLogBuilder WithFileSink(string path, string? minLevel = null) {
        _logFilePath = path;
        _logFileMinLevel = minLevel;
        _logFileKind = InferFileKind(path);
        return this;
    }

    /// <summary>Add a file sink with explicit kind (json_file, text_file, protobuf_file).</summary>
    public QuickLogBuilder WithFileSink(string path, string kind, string? minLevel = null) {
        _logFilePath = path;
        _logFileMinLevel = minLevel;
        _logFileKind = kind;
        return this;
    }

    /// <summary>Add a file sink with rolling file support.</summary>
    public QuickLogBuilder WithFileSink(
        string path,
        string interval,
        long? maxBytes = null,
        int? logQueueSize = null,
        int? maxRetainedFiles = null,
        int startMinute = 0,
        int captureDurationMinutes = 60,
        string? fileNameSuffix = null,
        string? locale = null,
        string? minLevel = null,
        int? retentionDays = null,
        long? totalSizeCapBytes = null) {
        // Preserve explicitly set kind if path hasn't changed
        if (_logFilePath != path)
            _logFileKind = InferFileKind(path);
        _logFilePath = path;
        _logFileMinLevel = minLevel;
        _logFileRolling = new JsonFileRollingConfig(
            Interval: interval,
            MaxBytes: maxBytes,
            MaxRetainedFiles: maxRetainedFiles,
            LogQueueSize: logQueueSize,
            StartMinute: startMinute,
            CaptureDurationMinutes: captureDurationMinutes,
            FileNameSuffix: fileNameSuffix,
            Locale: locale,
            RetentionDays: retentionDays,
            TotalSizeCapBytes: totalSizeCapBytes);
        return this;
    }

    private static string InferFileKind(string path) {
        if (path.EndsWith(".ndjson", System.StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jsonl", System.StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
            return Services.KnownSinkKinds.JsonFile;
        if (path.EndsWith(".pb", System.StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".proto", System.StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".bin", System.StringComparison.OrdinalIgnoreCase))
            return Services.KnownSinkKinds.ProtobufFile;
        return Services.KnownSinkKinds.TextFile;
    }

    /// <summary>Add a file sink with a fluent rolling policy.</summary>
    public QuickLogBuilder WithFileSink(string path, FileSinkPolicy policy) {
        return WithFileSink(path,
            interval: policy.Interval,
            maxBytes: policy.MaxBytes,
            logQueueSize: policy.LogQueueSize,
            maxRetainedFiles: policy.MaxRetainedFiles,
            startMinute: policy.StartMinute,
            captureDurationMinutes: policy.CaptureDurationMinutes,
            fileNameSuffix: policy.FileNameSuffix,
            locale: policy.Locale,
            minLevel: policy.MinLevel,
            retentionDays: policy.RetentionDays,
            totalSizeCapBytes: policy.TotalSizeCapBytes);
    }

    // -- Sink labels --

    /// <summary>
    /// Attach a label to the sink registered under <paramref name="sinkName"/>.
    /// The label is the runtime identifier the security registrar consults
    /// when an external caller registers a key for one or more sinks; it is
    /// kept distinct from the config-time <paramref name="sinkName"/> so a
    /// caller cannot guess a sink's label from its name alone.
    ///
    /// <para>
    /// Pass an empty string to keep the entry but defer to the auto-
    /// generator at hoist time — the gate emits a random 32-hex-char label
    /// then. Pass any non-empty value to use that string verbatim. Sinks
    /// not registered here have <c>Label = null</c> on their config,
    /// which the gate also auto-generates.
    /// </para>
    /// </summary>
    public QuickLogBuilder WithSinkLabel(string sinkName, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sinkName);
        System.ArgumentNullException.ThrowIfNull(label);
        _sinkLabels[sinkName] = label;
        return this;
    }

    // -- Level and presentation --

    /// <summary>Set the minimum log level (trace, debug, info, warn, error, fatal).</summary>
    public QuickLogBuilder WithMinimumLevel(string level) {
        _minimumLevel = level;
        return this;
    }

    /// <summary>Dump registered log levels to console at startup.</summary>
    public QuickLogBuilder WithLevelDump() {
        _dumpLevels = true;
        return this;
    }

    // -- Time and signals --

    /// <summary>Set a custom time provider for deterministic testing.</summary>
    public QuickLogBuilder WithTimeProvider(IDateTimeProvider timeProvider) {
        _timeProvider = timeProvider;
        return this;
    }

    /// <summary>Set a global signal handler applied to all sinks.</summary>
    public QuickLogBuilder WithSignalHandler(ILogSignalHandler handler) {
        _globalSignalHandler = handler;
        return this;
    }

    /// <summary>Enable W3C trace context correlation.</summary>
    public QuickLogBuilder WithTraceCorrelation() {
        _includeActivityContext = true;
        return this;
    }

    // -- Pipeline strategy --

    /// <summary>
    /// Set a custom pipeline strategy that controls the order of decorators.
    /// Without this, the default strategy is used (flight-recorder-friendly).
    /// See <see cref="Configuration.PipelineStrategy"/> for presets and custom ordering.
    /// </summary>
    public QuickLogBuilder WithPipelineStrategy(Configuration.PipelineStrategy strategy) {
        _pipelineStrategy = strategy;
        return this;
    }

    // -- Plugin registration --

    /// <summary>
    /// Force-load a plugin assembly so its <c>[ModuleInitializer]</c> runs
    /// before the builder constructs the pipeline. This is the supported
    /// way to register plugin-supplied pipeline steps (HotPath, FlightRecorder,
    /// CircuitBreaker, Retry, DurableBuffer, Fallback, Audit, EventProcessing)
    /// when the strategy comes from JSON config and no plugin extension
    /// method is called from user code.
    ///
    /// <para>
    /// Mechanism: taking the <c>typeof(TPlugin).Assembly</c> reference forces
    /// the runtime to load the plugin assembly if it isn't already, which
    /// triggers any <c>[ModuleInitializer]</c> methods in that assembly.
    /// Those initializers call <see cref="MMP.Herald.Configuration.PipelineStep.Register"/>
    /// to register the plugin's steps and
    /// <see cref="MMP.Herald.Pipeline.PipelineStepHandlerRegistry.Register"/>
    /// to register the matching handlers.
    /// </para>
    ///
    /// <para>
    /// Cost: a single static-field reference. No reflection, no allocation,
    /// AOT-clean. Idempotent — calling twice is a no-op because the assembly
    /// only loads once.
    /// </para>
    ///
    /// <para>
    /// Usage:
    /// <code>
    /// builder.WithPlugin&lt;MMP.Herald.Pro.PluginBootstrap&gt;()
    ///        .WithPlugin&lt;MMP.Herald.Enterprise.PluginBootstrap&gt;()
    ///        .WithConfigurationSection(...);  // safe to deserialize plugin steps now
    /// </code>
    /// </para>
    /// </summary>
    /// <typeparam name="TPlugin">Any type defined in the plugin assembly.
    /// By convention, plugins expose a <c>PluginBootstrap</c> type for this
    /// purpose so call sites read clearly.</typeparam>
    public QuickLogBuilder WithPlugin<TPlugin>() {
        _ = typeof(TPlugin).Assembly;
        return this;
    }

    // -- Level styles --

    /// <summary>
    /// Override the display style for a log level. Affects console ANSI rendering.
    /// Use this to fix contrast issues or customize the look per environment.
    ///
    /// Defaults (contrast-correct):
    ///   Trace = DimGray, Debug = Gray, Info = Green,
    ///   Warn = Gold (bold), Error = Crimson (bold), Fatal = DarkRed (bold, italic)
    /// </summary>
    public QuickLogBuilder WithLevelStyle(
        string levelKey, string colorName,
        bool bold = false, bool italic = false,
        string? backgroundColorName = null) {
        _levelStyleOverrides ??= new List<Configuration.Json.JsonLogLevelStyleConfig>();
        // Remove existing override for same key
        _levelStyleOverrides.RemoveAll(s =>
            string.Equals(s.LevelKey, levelKey, StringComparison.OrdinalIgnoreCase));
        _levelStyleOverrides.Add(new Configuration.Json.JsonLogLevelStyleConfig(
            levelKey, colorName, bold, italic, backgroundColorName));
        return this;
    }

    /// <summary>Remove a level style override (revert to default for that level).</summary>
    public QuickLogBuilder WithoutLevelStyle(string levelKey) {
        _levelStyleOverrides?.RemoveAll(s =>
            string.Equals(s.LevelKey, levelKey, StringComparison.OrdinalIgnoreCase));
        return this;
    }

    /// <summary>Clear all level style overrides (revert to defaults).</summary>
    public QuickLogBuilder ClearLevelStyles() {
        _levelStyleOverrides?.Clear();
        return this;
    }

    // -- Aliases (human-readable names for dashboard display) --

    /// <summary>
    /// Set a human-readable alias for a pipeline step, sink, or filter.
    /// The alias is displayed in the Dashboard instead of the technical ID.
    /// </summary>
    public QuickLogBuilder WithAlias(string id, string alias) {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        _aliases ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _aliases[id] = alias;
        return this;
    }

    /// <summary>Remove an alias.</summary>
    public QuickLogBuilder WithoutAlias(string id) {
        _aliases?.Remove(id);
        return this;
    }

    /// <summary>Get the alias for an ID, or null if not set.</summary>
    public string? GetAlias(string id) {
        if (_aliases is null) return null;
        return _aliases.TryGetValue(id, out var alias) ? alias : null;
    }

    /// <summary>Get all aliases as a read-only dictionary.</summary>
    public IReadOnlyDictionary<string, string> GetAliases() {
        if (_aliases is null) return new Dictionary<string, string>();
        return _aliases;
    }

    // -- Pipeline decorators (plugin steps) --

    /// <summary>
    /// Register a custom pipeline decorator plugin. The decorator's StepName
    /// is automatically registered as a valid PipelineStep, so it can be used
    /// in custom strategies: .Custom("rateLimiter").
    ///
    /// The decorator is discoverable by the dashboard and configurable at runtime.
    /// </summary>
    public QuickLogBuilder WithPipelineDecorator(Pipeline.IConfigurablePipelineDecorator decorator) {
        ArgumentNullException.ThrowIfNull(decorator);
        // Auto-register the step name with the decorator's metadata
        Configuration.PipelineStep.Register(
            decorator.StepName,
            displayName: decorator.DisplayName,
            description: decorator.Description,
            help: BuildPluginHelp(decorator),
            vendor: GetVendorInfo(decorator));
        // Replace if same step name exists
        _pipelineDecorators.RemoveAll(d =>
            string.Equals(d.StepName, decorator.StepName, StringComparison.OrdinalIgnoreCase));
        _pipelineDecorators.Add(decorator);
        return this;
    }

    /// <summary>Remove a pipeline decorator by step name.</summary>
    public QuickLogBuilder WithoutPipelineDecorator(string stepName) {
        _pipelineDecorators.RemoveAll(d =>
            string.Equals(d.StepName, stepName, StringComparison.OrdinalIgnoreCase));
        return this;
    }

    /// <summary>Get a registered pipeline decorator by step name, or null.</summary>
    public Pipeline.IConfigurablePipelineDecorator? GetPipelineDecorator(string stepName) =>
        _pipelineDecorators.Find(d =>
            string.Equals(d.StepName, stepName, StringComparison.OrdinalIgnoreCase));

    /// <summary>All registered pipeline decorators.</summary>
    public IReadOnlyList<Pipeline.IConfigurablePipelineDecorator> PipelineDecorators => _pipelineDecorators;

    // -- Convenience: Enrichers (delegates to .Enrichers) --

    /// <summary>Convenience: add enrichers. For fine-grained control, use .Enrichers directly.</summary>
    public QuickLogBuilder WithEnrichers(params Enrichers.ILogEnricher[] enrichers) =>
        Enrichers.Add(enrichers);

    /// <summary>Convenience: clear all default enrichers.</summary>
    public QuickLogBuilder WithoutDefaultEnrichers() =>
        Enrichers.Clear();

    /// <summary>
    /// Opt in to the system-tag enrichers (MachineName, ProcessId, ThreadId).
    /// Default was flipped on 2026-04-20 so the accepted-call path stays
    /// fast — callers who want the system tags now request them explicitly.
    /// Shortcut for <c>Enrichers.Reset()</c>.
    /// </summary>
    public QuickLogBuilder WithSystemTagsEnrichers() =>
        Enrichers.Reset();

    /// <summary>
    /// Reserve a per-tenant projection filter on the pipeline. The runtime
    /// shape — fielded predicate evaluated against the event's property
    /// bag, with overlay precedence (tenant rule overrides global rule
    /// for the same property name) — is not yet implemented; the call
    /// records the rule so consumers can write their pipeline
    /// configuration against the eventual shape.
    ///
    /// <para><b>Today:</b> calling this method records the rule and is
    /// otherwise a no-op until the projection-filter pipeline step
    /// lands. Herald.OSS does not enforce an edition gate on this
    /// surface; downstream commercial wrappers may layer their own
    /// gate on top.</para>
    ///
    /// <para><b>Why it ships now:</b> the gap analysis flagged
    /// per-tenant projection filtering as the missing operational
    /// lever for multi-tenant operators. Reserving the surface keeps
    /// pipeline configurations forward-compatible — operators can
    /// write the calls today, the implementation lands without a
    /// breaking change.</para>
    /// </summary>
    /// <param name="propertyName">Property name to filter on.</param>
    /// <param name="rule">Free-form rule expression (placeholder format
    /// while the runtime shape is being designed).</param>
    public QuickLogBuilder WithProjectionFilter(string propertyName, string rule)
    {
        System.ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        System.ArgumentException.ThrowIfNullOrWhiteSpace(rule);

        // Parse + validate at registration time so the operator sees
        // a malformed rule before any events flow. The parsed rule is
        // kept alongside the raw form so the pipeline assembly can
        // build the predicate without re-parsing on every event.
        var parsed = Filters.Projection.ProjectionFilterRule.Parse(propertyName, rule);
        _projectionFilterRules.Add((propertyName, rule, parsed));

        // Install (or reinstall) the composed predicate as a filter.
        // Resolution order, most specific first:
        //   1. Pipeline rules (this builder's _projectionFilterRules)
        //   2. Tenant rules (HeraldGlobalFilters per-tenant)
        //   3. Global rules (HeraldGlobalFilters all-tenants)
        // The merge happens by property name — a more specific layer's
        // rule for the same property name replaces the less specific.
        // A WithCustomFilter call AFTER the last WithProjectionFilter
        // still replaces the slot; that's the same single-slot
        // semantics WithCustomFilter already documents.
        ReinstallProjectionFilter();
        return this;
    }

    /// <summary>
    /// Resolve the effective projection-filter predicate for this
    /// builder against the current tenant scope and install it. Called
    /// from <see cref="WithProjectionFilter"/> on every rule add and
    /// from <see cref="ReresolveProjectionFilterAfterTenantChange"/>
    /// when the builder is registered to a tenant after some rules
    /// were added at the default-tenant scope.
    /// </summary>
    private void ReinstallProjectionFilter()
    {
        var pipelineRules = ParsedProjectionFilterRules;
        if (pipelineRules.Count == 0) return;

        // Build the resolved set: start with overlay rules for the
        // current tenant scope (HeraldGlobalFilters resolves
        // tenant-overrides-global per property name), then override
        // with the pipeline rules per property name.
        var tenant = HeraldTenantScope.Current;
        var overlay = Filters.Projection.HeraldGlobalFilters.GetEffectiveRulesFor(tenant);

        var resolved = new System.Collections.Generic.Dictionary<string, Filters.Projection.ProjectionFilterRule>(System.StringComparer.Ordinal);
        for (var i = 0; i < overlay.Count; i++) resolved[overlay[i].Property] = overlay[i];
        for (var i = 0; i < pipelineRules.Count; i++) resolved[pipelineRules[i].Property] = pipelineRules[i];

        var list = new System.Collections.Generic.List<Filters.Projection.ProjectionFilterRule>(resolved.Count);
        foreach (var v in resolved.Values) list.Add(v);

        var combined = Filters.Projection.ProjectionFilterRule.Combine(list);
        _samplingFilter = new Filters.FuncLogFilter(combined);
    }

    private readonly System.Collections.Generic.List<(string Property, string Rule, Filters.Projection.ProjectionFilterRule Parsed)>
        _projectionFilterRules = new();

    /// <summary>Snapshot of projection-filter rules registered on this builder.</summary>
    public System.Collections.Generic.IReadOnlyList<(string Property, string Rule)>
        ProjectionFilterRules
    {
        get
        {
            var list = new System.Collections.Generic.List<(string, string)>(_projectionFilterRules.Count);
            foreach (var entry in _projectionFilterRules)
                list.Add((entry.Property, entry.Rule));
            return list;
        }
    }

    /// <summary>
    /// Snapshot of parsed projection-filter rules. Internal so the
    /// pipeline assembly can compose them into a predicate without
    /// re-parsing.
    /// </summary>
    internal System.Collections.Generic.IReadOnlyList<Filters.Projection.ProjectionFilterRule>
        ParsedProjectionFilterRules
    {
        get
        {
            var list = new System.Collections.Generic.List<Filters.Projection.ProjectionFilterRule>(_projectionFilterRules.Count);
            foreach (var entry in _projectionFilterRules)
                list.Add(entry.Parsed);
            return list;
        }
    }

    // -- Convenience: Property styles (delegates to .PropertyStyles) --

    /// <summary>Convenience: add a property style.</summary>
    public QuickLogBuilder WithPropertyStyle(
        string propertyName, string colorName, bool bold = false, bool italic = false,
        string? backgroundColorName = null) =>
        PropertyStyles.Add(propertyName, colorName, bold, italic, backgroundColorName);

    // -- Convenience: Category (channel) styles (delegates to .CategoryStyles) --

    /// <summary>Convenience: add a category (channel) style.</summary>
    public QuickLogBuilder WithCategoryStyle(
        string categoryName, string? colorName = null, bool bold = false, bool italic = false,
        string? backgroundColorName = null) =>
        CategoryStyles.Add(categoryName, colorName, bold, italic, backgroundColorName);

    // -- Convenience: Channels (delegates to .Channels) --

    /// <summary>Convenience: add a channel sink with a custom transformer and writer.</summary>
    public QuickLogBuilder WithChannelSink(
        string channelName, ILogOutputTransformer transformer, IRenderedLogOutputWriter writer) =>
        Channels.Add(channelName, transformer, writer);

    /// <summary>Convenience: add a channel sink with transformer, writer, and signal handler.</summary>
    public QuickLogBuilder WithChannelSink(
        string channelName, ILogOutputTransformer transformer,
        IRenderedLogOutputWriter writer, ILogSignalHandler signalHandler) =>
        Channels.Add(channelName, transformer, writer, signalHandler);

    /// <summary>Convenience: add a channel sink with default transformer and custom writer.</summary>
    public QuickLogBuilder WithChannelSink(string channelName, IRenderedLogOutputWriter writer) =>
        Channels.Add(channelName, writer);

    /// <summary>Convenience: add a channel sink with custom writer and signal handler.</summary>
    public QuickLogBuilder WithChannelSink(
        string channelName, IRenderedLogOutputWriter writer, ILogSignalHandler signalHandler) =>
        Channels.Add(channelName, writer, signalHandler);

    // -- Convenience: Audit sinks (delegates to .AuditSinks) --

    /// <summary>Convenience: add an audit sink that bypasses minimum level filtering.</summary>
    public QuickLogBuilder WithAuditSink(
        IRenderedLogOutputWriter writer,
        ILogOutputTransformer? transformer = null,
        ILogSignalHandler? signalHandler = null) =>
        AuditSinks.Add(writer, transformer, signalHandler);

    // -- Convenience: Event processors (delegates to .EventProcessors) --

    /// <summary>Convenience: register an event processor (named by type).</summary>
    public QuickLogBuilder WithEventProcessor(ILogEventProcessor processor) =>
        EventProcessors.Add(processor);

    /// <summary>Convenience: register a named event processor.</summary>
    public QuickLogBuilder WithEventProcessor(string name, ILogEventProcessor processor, bool protect = false) =>
        EventProcessors.Add(name, processor, protect);

    /// <summary>Convenience: add compiled redaction rules.</summary>
    public QuickLogBuilder WithCompiledRedaction(params CompiledRedactionRule[] rules) =>
        EventProcessors.AddRedaction(rules);

    /// <summary>
    /// Install kernel-aware redaction. Rules run on the property span before
    /// the <see cref="MMP.Herald.Pipeline.Kernel.LogEventBuffer"/> is
    /// constructed and stay on the kernel fast path — they do not register
    /// as <see cref="ILogEventProcessor"/> instances and do not move the
    /// pipeline off the compact dispatch path.
    ///
    /// <para>
    /// Trade-off vs <see cref="WithCompiledRedaction(CompiledRedactionRule[])"/>:
    /// the fast path supports only the exact-name + Remove / Mask / Hash
    /// subset of the rule API. Pattern rules (glob / regex), event-action
    /// rules (DropEvent / ReplaceMessage), and value-pattern conditions
    /// belong on the heavier <see cref="CompiledRedactionProcessor"/>;
    /// passing them here throws at builder time. Most production redaction
    /// shapes are exact-name only, so this is the dominant case.
    /// </para>
    ///
    /// <para>
    /// Both convenience methods can be combined when a pipeline has both
    /// a hot-path subset (handled here, kernel-eligible) and a long-tail
    /// subset (handled by <see cref="WithCompiledRedaction"/>, event-pipeline
    /// path). The two redactors run independently — fast first, processor
    /// second — but a pipeline that uses both is no longer kernel-eligible
    /// because the event processor disqualifies it. The fast redactor
    /// still runs in that mode, but the structural floor is paid anyway,
    /// so combining the two only makes sense when the long tail is
    /// genuinely required.
    /// </para>
    /// </summary>
    public QuickLogBuilder WithFastRedaction(params CompiledRedactionRule[] rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.Length == 0) return this;

        _fastRedactionRules ??= new List<CompiledRedactionRule>(rules.Length);
        _fastRedactionRules.AddRange(rules);
        return this;
    }

    /// <summary>
    /// Install kernel-aware 1-in-N sampling. The sampler runs at the
    /// dispatch boundary before any <see cref="MMP.Herald.Pipeline.Kernel.LogEventBuffer"/>
    /// is constructed — dropped events pay only one
    /// <see cref="System.Threading.Interlocked.Increment(ref long)"/> and
    /// one modulo, no allocation, no kernel call.
    ///
    /// <para>
    /// Trade-off vs <see cref="WithSampling(int)"/>: the legacy filter
    /// reads from a fully-materialised <see cref="Events.LogEvent"/> and
    /// disqualifies the kernel fast path entirely. The fast sampler stays
    /// kernel-eligible, but only models the "1 in N for everything" case —
    /// per-category scopes, max-per-second throttling, and consistent-hash
    /// shapes still belong on <see cref="WithSampling"/> /
    /// <see cref="Filters.CompositeSamplingFilter"/>.
    /// </para>
    /// </summary>
    public QuickLogBuilder WithFastSampling(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate), sampleRate,
                "Sample rate must be greater than zero.");
        }
        _fastSampleRate = sampleRate;
        return this;
    }

    /// <summary>
    /// Append a fixed set of <see cref="MMP.Herald.Templating.LogProperty"/>
    /// entries to every accepted event on the kernel fast path. The
    /// enricher allocates one new property array per call (sized
    /// <c>input + properties.Length</c>) and never disqualifies the kernel.
    ///
    /// <para>
    /// Trade-off vs registering a custom <see cref="Enrichers.ILogEnricher"/>:
    /// the legacy enricher path mutates a heap-allocated
    /// <see cref="Events.LogEventEnrichmentContext"/> the chain materialises
    /// before the enricher runs. The fast path skips the chain entirely
    /// for static (constant-value) enrichment — host name, service name,
    /// deployment environment, fixed tag set. Dynamic enrichers that
    /// compute a value per call (traceId, request-scoped properties)
    /// stay on the legacy path until a separate experiment validates
    /// that shape.
    /// </para>
    /// </summary>
    public QuickLogBuilder WithFastEnrichment(params MMP.Herald.Templating.LogProperty[] properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (properties.Length == 0) return this;

        _fastEnrichmentProperties ??= new List<MMP.Herald.Templating.LogProperty>(properties.Length);
        _fastEnrichmentProperties.AddRange(properties);
        return this;
    }

    /// <summary>
    /// Install a kernel-aware dynamic-level resolver backed by a
    /// <see cref="MMP.Herald.Levels.LogLevelSwitch"/>. The switch's
    /// <see cref="MMP.Herald.Levels.LogLevelSwitch.MinimumLevel"/> can be
    /// mutated at runtime and every subsequent accepted call reads the
    /// current value via a single volatile-load + frozen-map lookup.
    ///
    /// <para>
    /// Trade-off vs <see cref="WithDynamicLevels()"/>: the legacy
    /// dynamic-level path goes through
    /// <see cref="MMP.Herald.Configuration.DynamicLevelPolicy"/>, which
    /// disqualifies the kernel fast path entirely. The fast resolver
    /// stays kernel-eligible. Per-category overrides
    /// (<see cref="MMP.Herald.Levels.CategoryLevelSwitchMap"/>) ride
    /// alongside the global switch via the second overload.
    /// </para>
    /// </summary>
    public QuickLogBuilder WithFastDynamicLevel(MMP.Herald.Levels.LogLevelSwitch levelSwitch)
    {
        ArgumentNullException.ThrowIfNull(levelSwitch);
        _fastDynamicLevelSwitch = levelSwitch;
        _fastDynamicLevelCategoryMap = null;
        return this;
    }

    /// <summary>
    /// Wrap this pipeline's sink fan-out in a kernel-aware async sink so
    /// the caller's <c>Log(...)</c> returns immediately once the buffer
    /// is materialised and enqueued — a background consumer drains the
    /// queue and forwards each event to the original sink composite.
    ///
    /// <para>
    /// <paramref name="boundedCapacity"/> sets the channel's drop-write
    /// threshold. Producers exceeding the cap have their events
    /// silently discarded by the channel (current accounting limitation
    /// — see future-direction.md A3 for the planned drop-counter fix).
    /// 4096 is a reasonable starting point for production traffic; the
    /// bench uses the same value for the head-to-head comparison.
    /// </para>
    ///
    /// <para>
    /// <b>Topology.</b> A single FastPathAsyncSink wraps the entire
    /// routed-sinks composite. One channel, one background consumer
    /// thread, sequential fan-out inside the consumer. A slow inner
    /// sink can stall the others because they share the consumer; if
    /// you need per-sink isolation, see future-direction.md for the
    /// per-sink-wrapper variant.
    /// </para>
    ///
    /// <para>
    /// <b>Hot-reload.</b> Each reload retires the current async wrapper
    /// (drain in-flight events on the old composite) and installs a new
    /// wrapper with the JSON-supplied capacity. No event is lost during
    /// the swap.
    /// </para>
    /// </summary>
    public QuickLogBuilder WithFastAsyncSink(int boundedCapacity = 4096)
    {
        if (boundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(boundedCapacity), boundedCapacity,
                "Bounded capacity must be greater than zero.");
        }
        _fastAsyncSinkCapacity = boundedCapacity;
        return this;
    }

    /// <summary>
    /// Install a kernel-aware dynamic-level resolver with both a global
    /// switch and a per-category override map. When an event's category
    /// has an override in <paramref name="categoryMap"/>, the override's
    /// minimum level governs acceptance for that event; otherwise the
    /// global <paramref name="levelSwitch"/> governs.
    ///
    /// <para>
    /// Both inputs are mutable at runtime — caller can flip the global
    /// switch's level and add / remove / change category-specific levels
    /// via <see cref="MMP.Herald.Levels.CategoryLevelSwitchMap.SetCategoryLevel"/>
    /// and <see cref="MMP.Herald.Levels.CategoryLevelSwitchMap.RemoveCategoryOverride"/>.
    /// The kernel reads through to the live state on every accepted call.
    /// </para>
    ///
    /// <para>
    /// Cost when categories are configured: one extra
    /// <c>ConcurrentDictionary.TryGetValue</c> per accepted event; categories
    /// with no override land back on the same global path the single-arg
    /// overload uses. Configurations with no map carry no extra cost (the
    /// resolver branches on a null field set at construction).
    /// </para>
    /// </summary>
    public QuickLogBuilder WithFastDynamicLevel(
        MMP.Herald.Levels.LogLevelSwitch levelSwitch,
        MMP.Herald.Levels.CategoryLevelSwitchMap categoryMap)
    {
        ArgumentNullException.ThrowIfNull(levelSwitch);
        ArgumentNullException.ThrowIfNull(categoryMap);
        _fastDynamicLevelSwitch = levelSwitch;
        _fastDynamicLevelCategoryMap = categoryMap;
        return this;
    }

    /// <summary>
    /// Convenience: install a <see cref="Addons.Observability.CardinalityGuardProcessor"/>
    /// that caps the distinct value count on the given properties. When a
    /// property exceeds its limit the visible value is replaced with the rule's
    /// sentinel; the original value is preserved as a silent property so JSON
    /// sinks keep the forensic trail. Pipelines that feed Prometheus / Grafana /
    /// Datadog should set this on high-fanout properties like userId or
    /// entityId to avoid exploding their label storage.
    /// </summary>
    public QuickLogBuilder WithCardinalityGuard(params Addons.Observability.CardinalityRule[] rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.Length == 0) return this;
        return EventProcessors.Add("cardinalityGuard", new Addons.Observability.CardinalityGuardProcessor(rules));
    }

    /// <summary>
    /// Convenience: install a <see cref="Addons.Reduction.WindowedMeanStepHandler"/>
    /// that absorbs high-volume numeric events into per-window summary
    /// events. One rule = one (category prefix, property name, window size)
    /// triple. Originals that match a rule are not forwarded; the
    /// synthesized summary fires once per window with mean / count / min /
    /// max. Pipelines that emit one event per simulation tick or per
    /// HTTP request can drop downstream volume by the window size while
    /// keeping the aggregate signal.
    ///
    /// <para>
    /// The reduction step registers globally on
    /// <see cref="Pipeline.PipelineStepHandlerRegistry"/>. Strategies that
    /// include the step name <c>"windowedMean"</c> in their assembly
    /// order pick it up automatically.
    /// </para>
    /// </summary>
    public QuickLogBuilder WithWindowedMean(params Addons.Reduction.WindowedMeanRule[] rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.Length == 0) return this;

        // Register the step name so PipelineStrategy can include it via .Step("windowedMean").
        Configuration.PipelineStep.Register(
            Addons.Reduction.WindowedMeanStepHandler.StepNameKey,
            displayName: "Windowed-Mean Reduction",
            description: "Absorbs high-volume numeric events into per-window summary events.",
            help: "Each rule says: for events in a category prefix carrying a numeric property, accumulate count/sum/min/max across a tumbling window of N events; emit one synthesized summary on close. Originals are not forwarded.");

        // Register the handler that installs the decorator at pipeline-assembly time.
        Pipeline.PipelineStepHandlerRegistry.Register(
            new Addons.Reduction.WindowedMeanStepHandler(rules));

        return this;
    }

    // -- Convenience: Sink providers (delegates to .SinkProviders) --

    /// <summary>Convenience: register a custom sink provider.</summary>
    public QuickLogBuilder WithCustomSinkProvider(ILogSinkProvider provider) =>
        SinkProviders.Add(provider);

    /// <summary>Convenience: register a custom sink provider with protection.</summary>
    public QuickLogBuilder WithCustomSinkProvider(ILogSinkProvider provider, bool protect) =>
        SinkProviders.Add(provider, protect);

    // -- Convenience: Bridges (delegates to .Bridges) --

    /// <summary>Convenience: bridge events into another pipeline.</summary>
    public QuickLogBuilder WithBridge(ILogger target) =>
        Bridges.Add(target);

    /// <summary>Convenience: bridge filtered events into another pipeline.</summary>
    public QuickLogBuilder WithBridge(ILogger target, Func<LogEvent, bool> filter) =>
        Bridges.Add(target, filter);

    // -- Pipeline policy configuration ────────────────────────────────

    /// <summary>Enable async logging with a bounded queue.</summary>
    public QuickLogBuilder WithAsyncLogging(
        int capacity = 1024,
        string dropStrategy = Services.KnownDropStrategies.DropWrite,
        bool deferRendering = false) {
        _asyncEnabled = true;
        _asyncCapacity = capacity > 0 ? capacity : Services.PipelineDefaults.AsyncCapacity;
        _asyncDropStrategy = dropStrategy;
        _asyncDeferRendering = deferRendering;
        return this;
    }

    /// <summary>Disable async logging (synchronous pipeline).</summary>
    public QuickLogBuilder WithoutAsyncLogging() {
        _asyncEnabled = false;
        return this;
    }

    /// <summary>
    /// Enable deferred message-template rendering without enabling async. The event
    /// carries the raw template and properties through the pipeline; the rendering
    /// step formats into a string only when it reaches a sink that needs text.
    /// Sinks that consume structured output (JSON, OTLP, null) skip rendering
    /// entirely. Combine with <c>WithAsyncLogging</c> to also move rendering off
    /// the calling thread.
    /// </summary>
    public QuickLogBuilder WithDeferredRendering() {
        _asyncDeferRendering = true;
        return this;
    }

    /// <summary>Enable event batching for efficient sink delivery.</summary>
    public QuickLogBuilder WithBatching(
        int maxBatchSize = 32,
        int maxBatchDelayMs = 1000) {
        _batchingEnabled = true;
        _batchMaxSize = maxBatchSize > 0 ? maxBatchSize : Services.PipelineDefaults.BatchSize;
        _batchDelayMs = maxBatchDelayMs > 0 ? maxBatchDelayMs : Services.PipelineDefaults.BatchDelayMs;
        return this;
    }

    /// <summary>Disable event batching.</summary>
    public QuickLogBuilder WithoutBatching() {
        _batchingEnabled = false;
        return this;
    }

    /// <summary>
    /// Enable the flight-recorder ring buffer. Events below
    /// <paramref name="minLevel"/> are buffered instead of dropped; when an
    /// event at or above <paramref name="triggerLevel"/> arrives the buffer
    /// drains to the inner pipeline before the trigger event.
    ///
    /// <para>
    /// Pass <c>null</c> for <paramref name="minLevel"/> to inherit the
    /// pipeline minimum, or <c>null</c> for <paramref name="triggerLevel"/>
    /// to use the conventional "error" trigger. Make sure the
    /// <c>flightRecorder</c> step appears in your pipeline strategy —
    /// the <see cref="Configuration.PipelineStrategy.Default"/> already
    /// places it before filtering.
    /// </para>
    ///
    /// <para>
    /// <b>Edition:</b> the decorator itself is Community. The canonical
    /// shape — <c>bufferSize: 200</c>, no minimum-level override, and a
    /// null-or-<c>"error"</c> trigger — is free to use. Any deviation
    /// (larger buffer, different trigger level, explicit minimum-level
    /// override) requires the Pro edition; the gate fires here via
    /// <see cref="Configuration.FlightRecorderCommunityShape.RequireConfigurabilityEdition"/>.
    /// </para>
    /// </summary>
    public QuickLogBuilder WithFlightRecorder(
        int bufferSize = 200,
        string? minLevel = null,
        string? triggerLevel = null) {
        if (bufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be greater than zero.");

        _flightRecorderEnabled = true;
        _flightRecorderBufferSize = bufferSize;
        _flightRecorderMinLevel = minLevel;
        _flightRecorderTriggerLevel = triggerLevel;
        return this;
    }

    /// <summary>Disable the flight-recorder ring buffer.</summary>
    public QuickLogBuilder WithoutFlightRecorder() {
        _flightRecorderEnabled = false;
        return this;
    }

    /// <summary>
    /// Enable post-filtering: events are buffered and filtered as a batch.
    /// When any event in the buffered batch matches
    /// <paramref name="triggerCondition"/>, the
    /// <paramref name="escalatedFilter"/> applies to the whole batch;
    /// otherwise the <paramref name="normalFilter"/> applies. Mirrors
    /// NLog's PostFilteringWrapper.
    ///
    /// <para>
    /// All three predicates are required. Pass them as
    /// <see cref="PredicateSpec"/> records — the same shape that JSON config
    /// uses, so the builder and JSON paths produce identical pipelines.
    /// </para>
    /// </summary>
    public QuickLogBuilder WithPostFiltering(
        PredicateSpec triggerCondition,
        PredicateSpec normalFilter,
        PredicateSpec escalatedFilter,
        int maxBatchSize = 0,
        int maxBatchDelayMs = 0) {
        ArgumentNullException.ThrowIfNull(triggerCondition);
        ArgumentNullException.ThrowIfNull(normalFilter);
        ArgumentNullException.ThrowIfNull(escalatedFilter);

        _postFilteringEnabled = true;
        _postFilteringTrigger = triggerCondition;
        _postFilteringNormal = normalFilter;
        _postFilteringEscalated = escalatedFilter;
        _postFilteringMaxBatchSize = maxBatchSize > 0 ? maxBatchSize : Services.PipelineDefaults.BatchSize;
        _postFilteringMaxBatchDelayMs = maxBatchDelayMs > 0 ? maxBatchDelayMs : Services.PipelineDefaults.BatchDelayMs;
        return this;
    }

    /// <summary>Disable post-filtering.</summary>
    public QuickLogBuilder WithoutPostFiltering() {
        _postFilteringEnabled = false;
        _postFilteringTrigger = null;
        _postFilteringNormal = null;
        _postFilteringEscalated = null;
        return this;
    }

    /// <summary>Enable hot-reload pipeline swapping at runtime.</summary>
    public QuickLogBuilder WithHotReload(int debounceMs = 500) {
        _hotReloadEnabled = true;
        _hotReloadDebounceMs = debounceMs;
        return this;
    }

    /// <summary>Disable hot-reload.</summary>
    public QuickLogBuilder WithoutHotReload() {
        _hotReloadEnabled = false;
        return this;
    }

    /// <summary>Enable caller info capture (method name, file, line number).</summary>
    public QuickLogBuilder WithCallerInfo() {
        _includeCallerInfo = true;
        return this;
    }

    /// <summary>Disable caller info capture.</summary>
    public QuickLogBuilder WithoutCallerInfo() {
        _includeCallerInfo = false;
        return this;
    }

    /// <summary>Enable dynamic level switching at runtime with optional per-category overrides.</summary>
    public QuickLogBuilder WithDynamicLevels() {
        _dynamicLevelsEnabled = true;
        return this;
    }

    /// <summary>Add a per-category level override for dynamic level filtering.</summary>
    public QuickLogBuilder WithCategoryLevelOverride(string category, string level) {
        _dynamicLevelsEnabled = true;
        _categoryLevelOverrides ??= new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        _categoryLevelOverrides[category] = level;
        return this;
    }

    /// <summary>Enable random sampling: keep 1 in every N events.</summary>
    public QuickLogBuilder WithSampling(int sampleRate) {
        _samplingRate = sampleRate > 0 ? sampleRate : 1;
        _samplingFilter = new Filters.SamplingFilter(sampleRate);
        return this;
    }

    /// <summary>Disable sampling.</summary>
    public QuickLogBuilder WithoutSampling() {
        _samplingRate = 0;
        _samplingFilter = null;
        return this;
    }

    /// <summary>
    /// Register a per-type destructuring projection for <typeparamref name="T"/>.
    /// Values of the type match the resulting <see cref="MMP.Herald.Templating.IDestructuringPolicy"/>
    /// first; unmatched types fall through to the next registered policy,
    /// then to the built-in <c>ToString</c> fallback.
    /// </summary>
    /// <remarks>
    /// Ergonomic parallel to Serilog's
    /// <c>.Destructure.ByTransforming&lt;T&gt;(r =&gt; new { r.Path, r.Method })</c>.
    /// Policies are evaluated in registration order; register the most
    /// specific types first. The projection runs only when a value of
    /// <typeparamref name="T"/> reaches the <c>{@Name}</c> capture mode
    /// during rendering, so configuring a policy for a rarely-logged
    /// type has zero cost on the rest of the pipeline.
    /// </remarks>
    public QuickLogBuilder Destructure<T>(Func<T, object?> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        // Thread the active complex-value serializer through to the
        // policy. Defaults to ToStringComplexValueSerializer (AOT-clean);
        // consumers install a richer plugin via WithComplexValueSerializer
        // before this call to override.
        _destructuringPolicies.Add(
            new Templating.TransformDestructuringPolicy<T>(projection, _complexValueSerializer));
        return this;
    }

    /// <summary>
    /// Register a pre-built <see cref="MMP.Herald.Templating.IDestructuringPolicy"/>.
    /// Use this when the built-in type-projection form isn't expressive enough
    /// — e.g. a single policy that matches several related types, or one that
    /// needs to inspect runtime state the projection form cannot see.
    /// </summary>
    public QuickLogBuilder WithDestructuringPolicy(Templating.IDestructuringPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _destructuringPolicies.Add(policy);
        return this;
    }

    /// <summary>
    /// Install an <see cref="MMP.Herald.Templating.IComplexValueSerializer"/>
    /// for rendering destructured projection output. Defaults to
    /// <see cref="MMP.Herald.Templating.ToStringComplexValueSerializer"/>
    /// — AOT-clean, renders via <c>ToString</c>. Plugins from
    /// <c>Herald.Plugins</c> (e.g.
    /// <c>MMP.Herald.Plugins.Serialization.Reflection</c>) supply richer
    /// implementations: graceful per-subtree depth limiting, real
    /// structured JSON output, source-generated trim-safe rendering.
    /// </summary>
    /// <remarks>
    /// Plugins typically ship a one-line builder extension method (e.g.
    /// <c>.WithReflectionDestructuring()</c>); this method is the
    /// underlying API both extensions and direct consumers use. Call
    /// <em>before</em> any <c>Destructure&lt;T&gt;(...)</c> registrations
    /// so the policies pick up the new serializer; calling it after has
    /// no effect on policies already in the chain.
    /// </remarks>
    public QuickLogBuilder WithComplexValueSerializer(
        Templating.IComplexValueSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        _complexValueSerializer = serializer;
        return this;
    }

    /// <summary>
    /// Register a policy that short-circuits the destructuring chain
    /// for values of type <typeparamref name="T"/>, rendering them
    /// through <c>ToString</c> instead. Parallels Serilog's
    /// <c>.Destructure.AsScalar&lt;T&gt;()</c>.
    /// </summary>
    /// <remarks>
    /// Right when a parent-type policy (e.g.
    /// <c>Destructure&lt;IEntity&gt;(...)</c>) would otherwise project a
    /// specific subtype and you want that subtype to fall through to
    /// its own <c>ToString</c>. Register the AsScalar entry
    /// <em>before</em> the parent — the chain is first-match-wins.
    /// </remarks>
    public QuickLogBuilder WithDestructuringAsScalar<T>()
    {
        _destructuringPolicies.Add(new Templating.AsScalarDestructuringPolicy<T>());
        return this;
    }

    /// <summary>
    /// Install an <see cref="Addons.Query.ExpressionLogFilter"/> that
    /// gates the pipeline on a query-DSL predicate. Shape parallels
    /// Serilog's <c>Filter.ByIncludingOnly("...")</c>; the DSL is the
    /// one <see cref="Addons.Query.LogEventQuery"/> already parses for
    /// search and archive predicates.
    /// </summary>
    /// <remarks>
    /// Plugs into the same pipeline position sampling uses — the
    /// <see cref="Filters.ILogFilter"/> slot consumed by
    /// <see cref="Filters.FilteringLogger"/>. Rejections attribute to
    /// <c>DropReasons.Predicate</c> in the metrics surface.
    /// Calling this replaces any previously-set sampling or expression
    /// filter in that slot; combine multiple predicates with
    /// <c>AND</c> / <c>OR</c> inside one expression.
    /// </remarks>
    public QuickLogBuilder WithFilterExpression(string expression) {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        _samplingFilter = new Addons.Query.ExpressionLogFilter(expression);
        return this;
    }

    /// <summary>
    /// Install a custom predicate on the pipeline's filter slot.
    /// Shape parallels Serilog's <c>Filter.With(ILogEventFilter)</c>;
    /// pass any <see cref="Func{LogEvent, Boolean}"/> — return
    /// <c>true</c> to keep the event, <c>false</c> to drop it.
    /// </summary>
    /// <remarks>
    /// Right escape hatch when the filter expression grammar is too
    /// narrow (no projection, no <c>coalesce</c>, no array indexing)
    /// AND the predicate is a one-off — for filters where the value
    /// you want to test is reused across many predicates, prefer an
    /// enricher that pre-computes the value as a property.
    /// <para>
    /// AOT-clean — plain delegate dispatch, no reflection or
    /// expression-tree compilation. Plugs into the same pipeline
    /// position sampling and <c>WithFilterExpression</c> use; calling
    /// this replaces any previously-set filter in that slot. Combine
    /// multiple predicates inside the lambda itself.
    /// </para>
    /// </remarks>
    public QuickLogBuilder WithCustomFilter(Func<LogEvent, bool> predicate) {
        ArgumentNullException.ThrowIfNull(predicate);
        _samplingFilter = new Filters.FuncLogFilter(predicate);
        return this;
    }

    /// <summary>Register a custom log level.</summary>
    public QuickLogBuilder WithCustomLevel(string key, string displayName) {
        _customLevels ??= new List<Levels.LogLevel>();
        // Replace if same key already exists
        _customLevels.RemoveAll(l => string.Equals(l.Key, key, StringComparison.OrdinalIgnoreCase));
        _customLevels.Add(new Levels.LogLevel(key, displayName));
        return this;
    }

    /// <summary>Remove a custom log level by key.</summary>
    public QuickLogBuilder WithoutCustomLevel(string key) {
        _customLevels?.RemoveAll(l => string.Equals(l.Key, key, StringComparison.OrdinalIgnoreCase));
        return this;
    }

    /// <summary>Clear all custom log levels.</summary>
    public QuickLogBuilder ClearCustomLevels() {
        _customLevels?.Clear();
        return this;
    }

    /// <summary>Get all custom log levels (not including base levels).</summary>
    public IReadOnlyList<Levels.LogLevel> GetCustomLevels() =>
        _customLevels is not null ? new List<Levels.LogLevel>(_customLevels) : [];

    /// <summary>
    /// Set the level-key ordering used when the builder serializes its
    /// JSON configuration. The keys end up as the JSON config's
    /// <c>baseLevels</c> in this exact order, which the runtime uses
    /// to assign ranks. Pass <c>null</c> or an empty array to revert
    /// to the canonical default order.
    ///
    /// This is the seam the dashboard's drag-rearrange flow drives:
    /// the Categories tab's commit lands here via
    /// <see cref="MMP.Herald.Addons.ManagementApi.HeraldManagementApi.SetLevelOrder"/>,
    /// then a hot reload picks up the new JSON.
    /// </summary>
    public QuickLogBuilder WithLevelOrder(IEnumerable<string>? keys) {
        if (keys is null) {
            _levelOrder = null;
            return this;
        }
        var list = new List<string>();
        foreach (var k in keys) {
            if (!string.IsNullOrWhiteSpace(k)) list.Add(k);
        }
        _levelOrder = list.Count > 0 ? list : null;
        return this;
    }

    /// <summary>Returns the level-order override, or null when the
    /// builder is using the canonical default order.</summary>
    public IReadOnlyList<string>? GetLevelOrder() =>
        _levelOrder is null ? null : new List<string>(_levelOrder);

    // -- Network / integration sinks ─────────────────────────────────

    /// <summary>
    /// Add an HTTP JSON sink that POSTs batched events to an endpoint.
    /// <paramref name="headers"/> are sent on every request — typically used
    /// for <c>Authorization: Bearer ...</c> against tenant-aware backends.
    /// Values support <c>${ENV_VAR}</c> interpolation via JSON config so
    /// tokens stay out of committed files.
    /// </summary>
    public QuickLogBuilder WithHttpJsonSink(
        string endpoint,
        string? minLevel = null,
        IReadOnlyDictionary<string, string>? headers = null) {
        _networkSinks.Add(new NetworkSinkConfig(Services.KnownSinkKinds.HttpJson, Uri: endpoint, Host: null, Port: null, MinLevel: minLevel, Headers: headers));
        return this;
    }

    /// <summary>Add a TCP JSON Line sink that streams events over a TCP connection.</summary>
    public QuickLogBuilder WithTcpJsonLineSink(string host, int port, string? minLevel = null) {
        _networkSinks.Add(new NetworkSinkConfig(Services.KnownSinkKinds.TcpJsonLine, Uri: null, Host: host, Port: port, MinLevel: minLevel));
        return this;
    }

    /// <summary>
    /// Add a UDP JSON Line sink that sends each event as a fire-and-forget
    /// datagram. No retries, no acknowledgements — pair with a durable sink
    /// if every event must survive.
    /// </summary>
    public QuickLogBuilder WithUdpJsonLineSink(string host, int port, string? minLevel = null) {
        _networkSinks.Add(new NetworkSinkConfig(Services.KnownSinkKinds.UdpJsonLine, Uri: null, Host: host, Port: port, MinLevel: minLevel));
        return this;
    }

    /// <summary>
    /// Add an Elasticsearch sink. <paramref name="headers"/> ride on every
    /// indexing request — typically <c>Authorization: ApiKey ...</c> for
    /// Elastic Cloud or a Basic header for self-hosted clusters.
    /// </summary>
    public QuickLogBuilder WithElasticsearchSink(
        string clusterUrl,
        string? minLevel = null,
        IReadOnlyDictionary<string, string>? headers = null) {
        _networkSinks.Add(new NetworkSinkConfig(Services.KnownSinkKinds.Elasticsearch, Uri: clusterUrl, Host: null, Port: null, MinLevel: minLevel, Headers: headers));
        return this;
    }

    /// <summary>
    /// Add a Slack webhook sink. When <paramref name="minLevel"/> is null, the
    /// sink applies a deliberate default floor of <c>"error"</c> — Slack
    /// channels are a poor fit for debug / info chatter. Pass an explicit
    /// level to override (including a lower level for audit channels).
    /// </summary>
    public QuickLogBuilder WithSlackWebhookSink(string webhookUrl, string? minLevel = null) {
        _networkSinks.Add(new NetworkSinkConfig(Services.KnownSinkKinds.SlackWebhook, Uri: webhookUrl, Host: null, Port: null, MinLevel: minLevel ?? "error"));
        return this;
    }

    /// <summary>
    /// Add a generic webhook sink. When <paramref name="minLevel"/> is null,
    /// the sink applies a deliberate default floor of <c>"warn"</c> — generic
    /// webhooks (PagerDuty, Opsgenie, custom on-call bridges) are alert
    /// surfaces rather than log aggregators. <paramref name="headers"/> ride
    /// on every POST — typically used for service-specific auth tokens
    /// (PagerDuty integration keys, Opsgenie API keys, custom HMAC signatures).
    /// </summary>
    public QuickLogBuilder WithWebhookSink(
        string url,
        string? minLevel = null,
        IReadOnlyDictionary<string, string>? headers = null) {
        _networkSinks.Add(new NetworkSinkConfig(Services.KnownSinkKinds.GenericWebhook, Uri: url, Host: null, Port: null, MinLevel: minLevel ?? "warn", Headers: headers));
        return this;
    }

    // NOTE: The .WithWebhookSink(url, rules, minLevel) overload was removed
    // when the generic webhook sink moved to MMP.Herald.Sinks.GenericWebhook.
    // Consumers wanting a rules engine now call
    // GenericWebhookSinkRegistration.RegisterWithRules(registry, rules)
    // on the sink-provider registry instead. The URL-only overload stays
    // here because it only needs the "webhook" kind string, not the sink.

    /// <summary>
    /// Add an OTLP JSON sink for OpenTelemetry. <paramref name="headers"/>
    /// ride on every request — required by tenant-aware OTLP backends
    /// (Honeycomb, Grafana Cloud, OpenObserve, Traceway, Datadog OTLP) for
    /// Bearer-token or API-key auth.
    /// </summary>
    public QuickLogBuilder WithOtlpJsonSink(
        string endpoint,
        string? minLevel = null,
        IReadOnlyDictionary<string, string>? headers = null) {
        _networkSinks.Add(new NetworkSinkConfig(Services.KnownSinkKinds.OtlpJson, Uri: endpoint, Host: null, Port: null, MinLevel: minLevel, Headers: headers));
        return this;
    }

    /// <summary>
    /// Add an OTLP Protobuf sink for OpenTelemetry. <paramref name="headers"/>
    /// ride on every request — same role as on the JSON sink.
    /// </summary>
    public QuickLogBuilder WithOtlpProtobufSink(
        string endpoint,
        string? minLevel = null,
        IReadOnlyDictionary<string, string>? headers = null) {
        _networkSinks.Add(new NetworkSinkConfig(Services.KnownSinkKinds.OtlpProtobuf, Uri: endpoint, Host: null, Port: null, MinLevel: minLevel, Headers: headers));
        return this;
    }

    // -- Private helpers for plugin metadata ──────────────────────────

    /// <summary>
    /// Build help text for a plugin decorator from its schema.
    /// Lists configurable fields so the Dashboard can render them.
    /// </summary>
    private static string BuildPluginHelp(Pipeline.IConfigurablePipelineDecorator decorator)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(decorator.Description);

        var schema = decorator.ConfigurationSchema;
        if (schema.Count > 0)
        {
            sb.Append("\n\nConfigurable fields:");
            foreach (var field in schema)
            {
                sb.Append($"\n  - {field.Name} ({field.FieldType}");
                if (field.DefaultValue is not null)
                    sb.Append($", default: {field.DefaultValue}");
                sb.Append($"): {field.Description}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extract vendor info from the decorator. Uses the component's own VendorInfo
    /// if available (via IComponentMetadata), otherwise builds from assembly metadata.
    /// </summary>
    private static Pipeline.VendorInfo GetVendorInfo(Pipeline.IConfigurablePipelineDecorator decorator)
    {
        // If the decorator implements IComponentMetadata and provides non-default vendor info, use it
        if (decorator is Pipeline.IComponentMetadata meta && meta.Vendor != Pipeline.VendorInfo.ThirdParty)
            return meta.Vendor;

        // Otherwise, build from assembly metadata
        return Pipeline.VendorInfo.FromAssembly(decorator.GetType().Assembly);
    }
}
