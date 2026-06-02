#nullable enable

using System;
using System.Threading.Tasks;
using MMP.Herald.Output.Rendering.Themes;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Events;
using MMP.Herald.Serilog.Formatting;
using MMP.Herald.Serilog.Sinks;

namespace MMP.Herald.Serilog.Configuration;

/// <summary>
/// Fluent <c>WriteTo.*</c> translator: maps each Serilog sink call to the
/// equivalent <see cref="MMP.Herald.Quick.QuickLogBuilder"/> sink method.
///
/// <para>
/// DRY contract: every method here is a one-line forwarder. No if/loop/format
/// logic for building sink JSON belongs in this file. That work lives in
/// QuickLogBuilder. If you find yourself writing a conditional here, stop and
/// push the logic into the builder instead.
/// </para>
///
/// <para>
/// Floor() semantics:
/// <c>restrictedToMinimumLevel: Verbose</c> means "no per-sink restriction" in
/// Serilog — the sink inherits the pipeline floor. We pass <c>null</c> to the
/// Herald builder for this case, which has the same effect. Any other level
/// means "restrict this sink" and we pass the Herald key string.
/// </para>
/// </summary>
public sealed class LoggerSinkConfiguration
{
    private readonly LoggerConfiguration _root;

    // When true, Sink() routes user sinks to the audit list (throw on failure).
    // When false, routes to the write list (swallow on failure).
    // AuditTo is constructed with true; WriteTo is constructed with false.
    private readonly bool _defaultAuditMode;

    internal LoggerSinkConfiguration(LoggerConfiguration root, bool defaultAuditMode = false)
    {
        _root = root;
        _defaultAuditMode = defaultAuditMode;
    }

    // Ensures the shared SerilogSinkAdapter is created and registered exactly once,
    // regardless of whether WriteTo.Sink() or AuditTo.Sink() is called first.
    // Both channels share the same adapter so they occupy a single "null" JSON slot.
    private SerilogSinkAdapter EnsureSharedAdapter(LogEventLevel restrictedToMinimumLevel)
    {
        if (_root.SharedSinkAdapter is null)
        {
            var adapter = new SerilogSinkAdapter(_root.SerilogPolicyApplicator);
            _root.SharedSinkAdapter = adapter;
            _root.Builder.WithNullSink(minLevel: Floor(restrictedToMinimumLevel));
            _root.Builder.WithCustomSinkProvider(adapter);
        }
        return _root.SharedSinkAdapter;
    }

    // Maps Verbose → null (inherit pipeline floor), anything else → key string.
    // NOTE: restrictedToMinimumLevel:Verbose = "no per-sink restriction" in Serilog
    // (the sink accepts all events the pipeline lets through). Passing null to
    // Herald's minLevel parameter has the same meaning — no additional sink floor.
    private static string? Floor(LogEventLevel level)
        => level == LogEventLevel.Verbose ? null : SerilogLevelMap.ToHerald(level).Key;

    /// <summary>
    /// Add a standard console sink.
    /// Maps to <c>QuickLogBuilder.WithConsoleSink(minLevel: ...)</c>.
    /// </summary>
    public LoggerConfiguration Console(
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose)
    {
        _root.Builder.WithConsoleSink(minLevel: Floor(restrictedToMinimumLevel));
        return _root;
    }

    /// <summary>
    /// W4: add a themed (colorizing) console sink. Mirrors Serilog's
    /// <c>WriteTo.Console(theme: AnsiConsoleTheme.*)</c>: each event's level drives
    /// the ANSI styling supplied by <paramref name="theme"/>.
    ///
    /// <para>
    /// Reachable on net8/net9/net10 — it rides a themed <see cref="ITextFormatter"/>
    /// that carries no kernel-buffer dependency, so it sits outside the net9 gate on
    /// the user-formatter <c>Console(ITextFormatter)</c> overload.
    /// </para>
    ///
    /// <para>
    /// <b>theme is required and leads the parameter list</b> (it has no default) so
    /// this overload never collides with the bare <c>Console()</c> / <c>Console(level)</c>
    /// verb above. Use <see cref="ConsoleTheme.None"/> or
    /// <see cref="ConsoleTheme.Grayscale"/> for byte-identical-to-unthemed output;
    /// ANSI is also suppressed automatically when the console output is redirected.
    /// </para>
    /// </summary>
    /// <param name="theme">The console theme that styles each level. Must not be null.</param>
    /// <param name="outputTemplate">
    /// The Serilog output-template string for the rendered text, or null for the
    /// Serilog default template.
    /// </param>
    /// <param name="restrictedToMinimumLevel">
    /// Minimum level for events routed to this sink. Defaults to
    /// <see cref="LogEventLevel.Verbose"/> (no per-sink restriction).
    /// </param>
    public LoggerConfiguration Console(
        IConsoleTheme theme,
        string? outputTemplate = null,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose)
    {
        ArgumentNullException.ThrowIfNull(theme);

        // Register the console sink in the JSON config (sets the kind + minLevel),
        // then override the default console provider with a themed one. Same
        // last-write-wins additional-provider path the ITextFormatter overload uses.
        _root.Builder.WithConsoleSink(minLevel: Floor(restrictedToMinimumLevel));
        _root.Builder.WithCustomSinkProvider(
            new ThemedConsoleSinkProvider(new ThemedConsoleTextFormatter(theme, outputTemplate)));

        return _root;
    }

    /// <summary>
    /// Add a console sink that routes each event through a user-supplied
    /// <see cref="ITextFormatter"/> instead of Herald's default console renderer.
    ///
    /// <para>
    /// The formatter receives a <see cref="MMP.Herald.Serilog.Events.LogEvent"/>
    /// (the Serilog-shaped P1 mirror) and writes its output to the provided
    /// <see cref="System.IO.TextWriter"/>. Herald's ANSI styling pipeline is
    /// bypassed — the formatter owns the final representation.
    /// </para>
    ///
    /// <para>
    /// Mirrors <c>Serilog.LoggerConfiguration.WriteTo.Console(ITextFormatter)</c>
    /// so output-sink implementations that inject a custom formatter compile
    /// unchanged against MMP.Herald.Serilog.
    /// </para>
    /// </summary>
    /// <param name="formatter">
    /// The formatter to apply to each log event before writing to the console.
    /// Must not be null.
    /// </param>
    /// <param name="restrictedToMinimumLevel">
    /// Minimum level for events routed to this sink. Defaults to
    /// <see cref="LogEventLevel.Verbose"/> (no per-sink restriction).
    /// </param>
    public LoggerConfiguration Console(
        ITextFormatter formatter,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        // Register the console sink in the JSON config (sets the kind + minLevel).
        _root.Builder.WithConsoleSink(minLevel: Floor(restrictedToMinimumLevel));

        // Override the default ConsoleSinkProvider with one that routes events
        // through the user formatter. The additional-provider path overwrites
        // the built-in "console" kind in the sink registry (last-write-wins).
        _root.Builder.WithCustomSinkProvider(new TextFormatterConsoleSinkProvider(formatter));

        return _root;
    }

    /// <summary>
    /// Add a file sink. Herald infers JSON or text output from the file extension:
    /// <c>.ndjson</c> / <c>.jsonl</c> / <c>.json</c> → JSON; anything else → text.
    /// Maps to <c>QuickLogBuilder.WithFileSink(...)</c> via <see cref="FileSinkVerbMapper"/>.
    /// <para>
    /// NOTE: Herald infers JSON/text output from file extension
    /// (.ndjson/.jsonl/.json → JSON, else text).
    /// Real Serilog <c>WriteTo.File</c> always writes rendered text regardless of extension.
    /// </para>
    /// <para>
    /// W1: the rolling/retention/size arguments mirror Serilog's <c>WriteTo.File</c>
    /// defaults. When all three are at their defaults the sink behaves exactly as the
    /// pre-W1 2-arg form (cheap native overload). Setting any of them forwards to the
    /// native rolling overload. <see cref="RollingInterval.Year"/> and
    /// <see cref="RollingInterval.Month"/> have no native equivalent and throw.
    /// </para>
    /// </summary>
    /// <param name="path">The file path. The extension drives JSON-vs-text inference.</param>
    /// <param name="restrictedToMinimumLevel">Per-sink floor; Verbose means no restriction.</param>
    /// <param name="rollingInterval">Rolling cadence (default <see cref="RollingInterval.Infinite"/>, no rolling).</param>
    /// <param name="retainedFileCountLimit">Max rolled files to keep (Serilog default 31), or null for unbounded.</param>
    /// <param name="fileSizeLimitBytes">Per-file size cap in bytes (Serilog default 1 GiB), or null for unbounded.</param>
    public LoggerConfiguration File(string path,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose,
        RollingInterval rollingInterval = RollingInterval.Infinite,
        int? retainedFileCountLimit = 31,
        long? fileSizeLimitBytes = 1L * 1024 * 1024 * 1024)
    {
        FileSinkVerbMapper.Apply(
            _root.Builder, path, restrictedToMinimumLevel,
            rollingInterval, retainedFileCountLimit, fileSizeLimitBytes);
        return _root;
    }

    /// <summary>
    /// Add an HTTP JSON sink that POSTs batched events to <paramref name="requestUri"/>.
    /// Maps to <c>QuickLogBuilder.WithHttpJsonSink(endpoint, minLevel: ...)</c>.
    /// </summary>
    public LoggerConfiguration Http(string requestUri,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose)
    {
        _root.Builder.WithHttpJsonSink(requestUri, minLevel: Floor(restrictedToMinimumLevel));
        return _root;
    }

    /// <summary>
    /// Add a TCP JSON Line sink.
    /// Maps to <c>QuickLogBuilder.WithTcpJsonLineSink(host, port, minLevel: ...)</c>.
    /// </summary>
    public LoggerConfiguration TCPSink(string host, int port,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose)
    {
        _root.Builder.WithTcpJsonLineSink(host, port, minLevel: Floor(restrictedToMinimumLevel));
        return _root;
    }

    /// <summary>
    /// Add a UDP JSON Line sink.
    /// Maps to <c>QuickLogBuilder.WithUdpJsonLineSink(host, port, minLevel: ...)</c>.
    /// </summary>
    public LoggerConfiguration UDPSink(string host, int port,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose)
    {
        _root.Builder.WithUdpJsonLineSink(host, port, minLevel: Floor(restrictedToMinimumLevel));
        return _root;
    }

    /// <summary>
    /// Add an Elasticsearch sink.
    /// Maps to <c>QuickLogBuilder.WithElasticsearchSink(clusterUrl, minLevel: ...)</c>.
    /// </summary>
    public LoggerConfiguration Elasticsearch(string clusterUrl,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose)
    {
        _root.Builder.WithElasticsearchSink(clusterUrl, minLevel: Floor(restrictedToMinimumLevel));
        return _root;
    }

    /// <summary>
    /// Add an OTLP JSON sink for OpenTelemetry.
    /// Maps to <c>QuickLogBuilder.WithOtlpJsonSink(endpoint, minLevel: ...)</c>.
    /// <para>
    /// NOTE: Herald uses JSON format (WithOtlpJsonSink).
    /// Real Serilog.Sinks.OpenTelemetry defaults to Protobuf (OTLP HTTP/protobuf).
    /// If Protobuf is required, use <see cref="OpenTelemetryProtobuf"/> instead.
    /// </para>
    /// </summary>
    public LoggerConfiguration OpenTelemetry(string endpoint,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose)
    {
        _root.Builder.WithOtlpJsonSink(endpoint, minLevel: Floor(restrictedToMinimumLevel));
        return _root;
    }

    /// <summary>
    /// Add an OTLP Protobuf sink for OpenTelemetry.
    /// Maps to <c>QuickLogBuilder.WithOtlpProtobufSink(endpoint, minLevel: ...)</c>.
    /// Use this when you need binary OTLP protocol compatibility.
    /// </summary>
    public LoggerConfiguration OpenTelemetryProtobuf(string endpoint,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose)
    {
        _root.Builder.WithOtlpProtobufSink(endpoint, minLevel: Floor(restrictedToMinimumLevel));
        return _root;
    }

    /// <summary>
    /// Route events to a user-authored <see cref="ILogEventSink"/>.
    ///
    /// <para>
    /// <strong>Hard boundary</strong>: this method works <em>only</em> for sinks you
    /// compile from source against <c>MMP.Herald.Serilog</c>. Pre-compiled community
    /// packages (<c>Serilog.Sinks.Seq</c>, <c>Serilog.Sinks.MSSqlServer</c>,
    /// <c>Serilog.Sinks.Datadog</c>, etc.) were compiled against the real Serilog
    /// assembly identity (<c>PublicKeyToken=24c2f752a8e58a10</c>), which this shim
    /// does not and cannot match. See the parity audit for the complete list of gaps.
    /// </para>
    ///
    /// <para>
    /// Multiple calls are supported — each sink is added to an internal fan-out list
    /// and all registered sinks receive every event. A single Herald "null" sink slot
    /// is used as the JSON config hook; calling <see cref="Null"/> after this method
    /// returns the same config root but does not add a second slot.
    /// </para>
    ///
    /// <para>
    /// Error handling: in normal mode, exceptions from a sink are swallowed so one
    /// misbehaving sink cannot crash the application (Task 5 wires SelfLog reporting).
    /// Set <paramref name="auditMode"/> to <c>true</c> to propagate exceptions
    /// immediately — Serilog's audit-sink semantics for delivery-guaranteed paths.
    /// </para>
    /// </summary>
    /// <param name="sink">The user sink to receive mirrored <see cref="Events.LogEvent"/> instances.</param>
    /// <param name="restrictedToMinimumLevel">
    /// Minimum level for events routed to this sink. Defaults to
    /// <see cref="LogEventLevel.Verbose"/> (no per-sink restriction).
    /// </param>
    /// <param name="auditMode">
    /// When <c>true</c>, exceptions from the sink propagate rather than being swallowed.
    /// Mirrors Serilog's audit-sink guarantee.  When called via <c>AuditTo.Sink</c> this
    /// defaults to <c>true</c>; when called via <c>WriteTo.Sink</c> it defaults to
    /// <c>false</c>.  Override only when you need to override the channel's default.
    /// </param>
    public LoggerConfiguration Sink(
        ILogEventSink sink,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose,
        bool? auditMode = null)
    {
        ArgumentNullException.ThrowIfNull(sink);

        // Resolve: explicit caller override wins; otherwise inherit the channel
        // default (_defaultAuditMode is true for AuditTo, false for WriteTo).
        var effectiveAuditMode = auditMode ?? _defaultAuditMode;

        // Ensure the shared adapter is created and registered once across both channels.
        var adapter = EnsureSharedAdapter(restrictedToMinimumLevel);

        // Route to the appropriate list based on mode.
        if (effectiveAuditMode)
            adapter.AddAudit(sink);
        else
            adapter.AddWrite(sink);

        return _root;
    }

    /// <summary>
    /// Add a native Herald sink by its <c>SinkKind</c> — the integration point for
    /// the 80+ <c>MMP.Herald.Sinks.*</c> packages, each of which auto-registers its
    /// provider into <see cref="MMP.Herald.Routing.LogSinkProviderRegistry.Default"/>
    /// at assembly load. Maps to
    /// <c>QuickLogBuilder.WithNetworkSink(kind, endpoint, minLevel: ...)</c>.
    ///
    /// <para>
    /// This is the fluent equivalent of naming the kind in <c>appsettings.json</c>'s
    /// <c>WriteTo</c>: the settings reader resolves an unknown <c>WriteTo[].Name</c>
    /// against the native registry and calls this verb. The <paramref name="endpoint"/>
    /// is the sink's push target (it lands in the sink definition's <c>Uri</c> slot);
    /// native network / integration sinks require one.
    /// </para>
    ///
    /// <para>
    /// No provider is resolved here — the kind is declared into the JSON config and
    /// the engine instantiates the matching provider at build time. A kind with no
    /// registered provider degrades exactly as an unknown kind would, the same as a
    /// direct <c>WithNetworkSink</c> call.
    /// </para>
    /// </summary>
    /// <param name="kind">The native sink kind (e.g. <c>"datadog"</c>, <c>"loki"</c>).</param>
    /// <param name="endpoint">The sink's push endpoint. Must be non-blank.</param>
    /// <param name="restrictedToMinimumLevel">
    /// Minimum level for events routed to this sink. Defaults to
    /// <see cref="LogEventLevel.Verbose"/> (no per-sink restriction).
    /// </param>
    public LoggerConfiguration Native(
        string kind,
        string endpoint,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose)
    {
        _root.Builder.WithNetworkSink(kind, endpoint, minLevel: Floor(restrictedToMinimumLevel));
        return _root;
    }

    /// <summary>
    /// Add a null sink that silently drops every event.
    /// Maps to <c>QuickLogBuilder.WithNullSink(minLevel: ...)</c>.
    /// Intended for benchmarks and configurations that want the pipeline
    /// present without downstream I/O.
    /// </summary>
    public LoggerConfiguration Null(
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose)
    {
        _root.Builder.WithNullSink(minLevel: Floor(restrictedToMinimumLevel));
        return _root;
    }

    // ── Sub-logger routing (WriteTo.Logger / WriteTo.Map / WriteTo.Async) ──────

    // Ensure the shared null-slot adapter exists, then register a sub-logger
    // route on it. Mirrors EnsureSharedAdapter for the user-sink path so all
    // Serilog-side secondary destinations share the single null slot.
    private void AddRoute(ISubLoggerRoute route)
    {
        var adapter = EnsureSharedAdapter(LogEventLevel.Verbose);
        adapter.AddRoute(route);
    }

    /// <summary>
    /// Route events into a nested sub-logger configured by <paramref name="configure"/>.
    /// Mirrors Serilog's <c>WriteTo.Logger(lc =&gt; ...)</c>: the lambda configures a
    /// child <see cref="LoggerConfiguration"/> (its own filters, enrichers, and sinks),
    /// and every event the parent emits is forwarded into that child pipeline.
    ///
    /// <para>
    /// The child owns its pipeline lifetime and is flushed when the parent closes —
    /// the shared null-slot logger is tracked by the pipeline as an async resource,
    /// so <c>CloseAndFlush[Async]</c> drains the child exactly once.
    /// </para>
    /// </summary>
    /// <param name="configure">Configures the child sub-logger. Must not be null.</param>
    /// <param name="restrictedToMinimumLevel">
    /// Per-route floor; <see cref="LogEventLevel.Verbose"/> means no restriction.
    /// The child pipeline applies its own minimum level independently.
    /// </param>
    public LoggerConfiguration Logger(
        Action<LoggerConfiguration> configure,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose)
    {
        ArgumentNullException.ThrowIfNull(configure);
        AddRoute(FixedSubLoggerRoute.Build(configure));
        return _root;
    }

    /// <summary>
    /// Async sink wrapper. Mirrors Serilog.Sinks.Async's <c>WriteTo.Async(a =&gt; ...)</c>.
    ///
    /// <para>
    /// Herald's pipeline is already async-buffered at the drain, so the explicit
    /// Async wrapper is a transparent pass-through: the inner sinks configured by
    /// <paramref name="configure"/> are registered on this same <c>WriteTo</c> and
    /// still receive asynchronous delivery from Herald's own buffer. Accepted for
    /// API compatibility so a migrated <c>WriteTo.Async(x =&gt; x.File(...))</c> call
    /// resolves and behaves correctly.
    /// </para>
    /// </summary>
    /// <param name="configure">Configures the wrapped (inner) sinks. Must not be null.</param>
    public LoggerConfiguration Async(Action<LoggerSinkConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        // Pass-through: inner sinks go on this same WriteTo accessor; Herald's
        // drain already provides the async delivery the wrapper would add.
        configure(this);
        return _root;
    }

    /// <summary>
    /// Generic dynamic routing. Mirrors Serilog.Sinks.Map's
    /// <c>WriteTo.Map&lt;TKey&gt;(keySelector, configure, sinkMapCountLimit)</c>:
    /// each event's key is computed by <paramref name="keySelector"/>, and the
    /// first time a key is seen its sub-logger is built lazily via
    /// <paramref name="configure"/> and cached. Subsequent events with that key
    /// reuse the cached sub-logger.
    ///
    /// <para>
    /// At most <paramref name="sinkMapCountLimit"/> distinct sub-loggers are kept;
    /// when the limit is reached the oldest is evicted (flushed + disposed). All
    /// live sub-loggers are flushed when the parent pipeline closes.
    /// </para>
    /// </summary>
    /// <typeparam name="TKey">The routing key type.</typeparam>
    /// <param name="keySelector">Computes the routing key for an event. Must not be null.</param>
    /// <param name="configure">Builds the sub-logger for a key. Must not be null.</param>
    /// <param name="sinkMapCountLimit">Max distinct live sub-loggers (default 10).</param>
    public LoggerConfiguration Map<TKey>(
        Func<MMP.Herald.Events.LogEvent, TKey> keySelector,
        Action<TKey, LoggerSinkConfiguration> configure,
        int sinkMapCountLimit = 10)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(configure);
        AddRoute(new MapSubLoggerRoute<TKey>(keySelector, configure, sinkMapCountLimit));
        return _root;
    }

    /// <summary>
    /// Property-name dynamic routing. Mirrors Serilog.Sinks.Map's
    /// <c>WriteTo.Map(keyPropertyName, defaultKey, configure, sinkMapCountLimit)</c>:
    /// the routing key is the value of the property named
    /// <paramref name="keyPropertyName"/> on each event, falling back to
    /// <paramref name="defaultKey"/> when the property is absent or null.
    /// </summary>
    /// <param name="keyPropertyName">The property whose value is the routing key. Must not be null.</param>
    /// <param name="defaultKey">Key used when the property is missing/null. Must not be null.</param>
    /// <param name="configure">Builds the sub-logger for a key. Must not be null.</param>
    /// <param name="sinkMapCountLimit">Max distinct live sub-loggers (default 10).</param>
    public LoggerConfiguration Map(
        string keyPropertyName,
        string defaultKey,
        Action<string, LoggerSinkConfiguration> configure,
        int sinkMapCountLimit = 10)
    {
        ArgumentNullException.ThrowIfNull(keyPropertyName);
        ArgumentNullException.ThrowIfNull(defaultKey);
        ArgumentNullException.ThrowIfNull(configure);

        return Map(
            logEvent => ResolveKey(logEvent, keyPropertyName, defaultKey),
            configure,
            sinkMapCountLimit);
    }

    /// <summary>
    /// Property-name dynamic routing without an explicit default key. Mirrors the
    /// Serilog.Sinks.Map overload <c>WriteTo.Map(keyPropertyName, configure, sinkMapCountLimit)</c>;
    /// an event missing the key property routes under the empty-string key.
    /// </summary>
    public LoggerConfiguration Map(
        string keyPropertyName,
        Action<string, LoggerSinkConfiguration> configure,
        int sinkMapCountLimit = 10)
        => Map(keyPropertyName, string.Empty, configure, sinkMapCountLimit);

    // Resolve the string routing key from an event: structured property first,
    // then context bag, then the default. One place owns the "where the key
    // lives" decision (DRY), matching Matching.WithProperty's lookup order.
    private static string ResolveKey(
        MMP.Herald.Events.LogEvent logEvent, string keyPropertyName, string defaultKey)
    {
        if (logEvent.GetProperty(keyPropertyName) is { } property)
            return property.ResolvedValue?.ToString() ?? defaultKey;

        if (logEvent.Context.TryGetValue(keyPropertyName, out var value))
            return value?.ToString() ?? defaultKey;

        return defaultKey;
    }

    /// <summary>
    /// Console sink overload that accepts Serilog's <c>standardErrorFromLevel</c>
    /// parameter. In Serilog this routes events at or above the given level to
    /// stderr (the rest to stdout).
    ///
    /// <para>
    /// Herald's console sink writes to stdout; the stdout/stderr split is not
    /// modelled, so <paramref name="standardErrorFromLevel"/> is accepted for API
    /// compatibility and the events still emit on the console. This keeps a
    /// migrated <c>WriteTo.Console(standardErrorFromLevel: ...)</c> call compiling
    /// and logging end-to-end; only the destination stream differs from Serilog.
    /// </para>
    /// </summary>
    /// <param name="standardErrorFromLevel">
    /// Serilog's stderr-routing floor. Accepted but not acted on (see remarks).
    /// </param>
    /// <param name="restrictedToMinimumLevel">
    /// Per-sink floor; <see cref="LogEventLevel.Verbose"/> means no restriction.
    /// </param>
    public LoggerConfiguration Console(
        LogEventLevel? standardErrorFromLevel,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose)
    {
        // standardErrorFromLevel is intentionally unused: Herald's console sink
        // emits on stdout. Accepted for API shape compatibility only.
        _root.Builder.WithConsoleSink(minLevel: Floor(restrictedToMinimumLevel));
        return _root;
    }

    /// <summary>
    /// Generic property-name dynamic routing. Mirrors Serilog.Sinks.Map's
    /// <c>WriteTo.Map&lt;TKey&gt;(keyPropertyName, configure, sinkMapCountLimit)</c>:
    /// the routing key is the value of the property named
    /// <paramref name="keyPropertyName"/> coerced to <typeparamref name="TKey"/>.
    /// An event missing the property (or whose value cannot be coerced) routes
    /// under <c>default(TKey)</c>.
    ///
    /// <para>
    /// This is the overload a migrated <c>WriteTo.Map&lt;bool&gt;("PrintStderr", ...)</c>
    /// binds to: the key property holds a typed value (here a bool) and each
    /// distinct value gets its own sub-logger.
    /// </para>
    /// </summary>
    /// <typeparam name="TKey">The routing key type the property value is coerced to.</typeparam>
    /// <param name="keyPropertyName">The property whose value is the routing key. Must not be null.</param>
    /// <param name="configure">Builds the sub-logger for a key. Must not be null.</param>
    /// <param name="sinkMapCountLimit">Max distinct live sub-loggers (default 10).</param>
    public LoggerConfiguration Map<TKey>(
        string keyPropertyName,
        Action<TKey, LoggerSinkConfiguration> configure,
        int sinkMapCountLimit = 10)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(keyPropertyName);
        ArgumentNullException.ThrowIfNull(configure);

        return Map(
            logEvent => ResolveTypedKey<TKey>(logEvent, keyPropertyName),
            configure,
            sinkMapCountLimit);
    }

    // Resolve a typed routing key from a named property. Looks in structured
    // Properties then the context bag (same order as the string forms), then
    // coerces to TKey. A direct cast covers the common case (the value already
    // is TKey); Convert.ChangeType covers primitive widening (e.g. boxed bool);
    // anything uncoercible falls back to default(TKey) so routing never throws.
    private static TKey ResolveTypedKey<TKey>(
        MMP.Herald.Events.LogEvent logEvent, string keyPropertyName)
        where TKey : notnull
    {
        object? raw = null;
        if (logEvent.GetProperty(keyPropertyName) is { } property)
            raw = property.ResolvedValue;
        else if (logEvent.Context.TryGetValue(keyPropertyName, out var ctx))
            raw = ctx;

        if (raw is TKey typed) return typed;

        if (raw is not null)
        {
            try
            {
                return (TKey)Convert.ChangeType(raw, typeof(TKey),
                    System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
            {
                // Uncoercible value — fall through to default so routing is total.
            }
        }

        return default!;
    }
}
