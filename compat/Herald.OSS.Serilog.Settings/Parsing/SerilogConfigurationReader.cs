// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using MMP.Herald.Routing;
using MMP.Herald.Serilog.Configuration;
using MMP.Herald.Serilog.Events;

namespace Herald.OSS.Serilog.Settings.Parsing;

/// <summary>
/// Reads a Serilog appsettings.json configuration section and applies it to a
/// <see cref="LoggerConfiguration"/> via the Layer-1 fluent API.
///
/// <para>
/// <b>Schema supported:</b>
/// <code>
/// {
///   "Serilog": {
///     "MinimumLevel": "Debug",                      // string shorthand
///     "MinimumLevel": {                              // OR object form
///       "Default": "Debug",
///       "Override": { "Microsoft.Extensions": "Warning" }
///     },
///     "WriteTo": [
///       { "Name": "Console" },
///       { "Name": "File", "Args": { "path": "log.txt" } }
///     ],
///     "Enrich": ["FromLogContext", "WithProperty"],  // Risk-10 string-array form
///     "Using": ["Serilog.Sinks.Seq"]                 // silently skipped
///   }
/// }
/// </code>
/// </para>
///
/// <para>
/// <b>WriteTo resolution contract:</b> an unresolved sink name THROWS
/// <see cref="SinkResolutionException"/> — never a silent no-op or SelfLog
/// write. A silent failure here means the application logs nowhere, which is
/// worse than a startup crash. (Pre-mortem Risk 2.)
/// </para>
///
/// <para>
/// <b>Registry bridge.</b> Resolution is two-stage. First the Layer-1 sink
/// registry (<see cref="LoggerSinkRegistry"/>) is consulted for the fluent
/// verbs (Console, File, Http, ...). When the name is not a Layer-1 verb, the
/// reader falls through to the native
/// <see cref="LogSinkProviderRegistry"/> by <c>SinkKind</c> (case-insensitive).
/// That second stage is what makes every <c>MMP.Herald.Sinks.*</c> package —
/// each of which auto-registers its provider into
/// <see cref="LogSinkProviderRegistry.Default"/> at assembly load — addressable
/// by Serilog <c>Name</c> in <c>appsettings.json</c> with no per-sink shim
/// code. The native registry is injected (defaulting to
/// <see cref="LogSinkProviderRegistry.Default"/>) so hosts and tests can supply
/// an isolated one. A name in neither registry still throws (Risk 2 preserved).
/// </para>
///
/// <para>
/// <b>Bridge mechanics.</b> The reader does NOT call
/// <c>ILogSinkProvider.CreateSink</c> directly — that would bypass the JSON
/// config that is Herald's source of truth for pipeline construction, and force
/// the reader to hand-build a level registry and transformer registry. Instead
/// it declares the kind into the builder via
/// <c>QuickLogBuilder.WithNetworkSink(kind, endpoint, ...)</c>; the engine
/// resolves the provider from the registry at build time, exactly as it does for
/// a host that called <c>WithNetworkSink</c> by hand. Native sinks reached this
/// way are network / integration sinks that push to an endpoint — the bridge
/// reads that endpoint from the WriteTo <c>Args</c> (<c>endpoint</c> /
/// <c>requestUri</c> / <c>uri</c> / <c>url</c>).
/// </para>
///
/// <para>
/// <b>Enrich resolution contract:</b> unresolved enricher names are skipped
/// (enrichers are optional pipeline decoration; a missing enricher does not
/// break log delivery). This asymmetry is intentional.
/// </para>
///
/// <para>
/// <b>Using section:</b> silently skipped. Assembly-loading is not supported
/// in this shim — community NuGet sinks compiled against the real Serilog
/// strong-name cannot be loaded regardless. Unresolved sink names in
/// <c>WriteTo</c> still throw via the resolution contract above.
/// </para>
/// </summary>
internal sealed class SerilogConfigurationReader
{
    private readonly LoggerConfiguration _loggerConfig;
    private readonly LoggerSinkRegistry _sinkRegistry;
    private readonly LoggerEnricherRegistry _enricherRegistry;

    // Native engine registry consulted when a WriteTo name is not a Layer-1 verb.
    // Defaults to the process-wide singleton that every MMP.Herald.Sinks.* package
    // auto-populates at assembly load. Injected so tests use an isolated instance.
    private readonly LogSinkProviderRegistry _nativeSinkRegistry;

    // Arg keys, in priority order, that carry a native network/integration sink's
    // push endpoint. WithNetworkSink lands this value in the sink definition's Uri
    // slot. Kept as a small ordered list rather than scattered ?? chains so the
    // accepted aliases live in one place. (Cognitive-complexity: one named list
    // beats a four-term coalescing expression at the call site.)
    private static readonly string[] EndpointArgKeys =
        { "endpoint", "requestUri", "uri", "url" };

    /// <summary>
    /// Initialise the reader with the target configuration and the registries
    /// used for name resolution.
    /// </summary>
    /// <param name="loggerConfig">The <see cref="LoggerConfiguration"/> to mutate.</param>
    /// <param name="sinkRegistry">Sink name-to-factory map (defaults to built-ins).</param>
    /// <param name="enricherRegistry">Enricher name-to-factory map (defaults to built-ins).</param>
    /// <param name="nativeSinkRegistry">
    ///   The native engine registry consulted when a <c>WriteTo</c> name is not a
    ///   Layer-1 verb (the registry bridge). Defaults to
    ///   <see cref="LogSinkProviderRegistry.Default"/> — the process-wide singleton
    ///   every <c>MMP.Herald.Sinks.*</c> package auto-populates at assembly load.
    ///   Pass an isolated instance from tests.
    /// </param>
    internal SerilogConfigurationReader(
        LoggerConfiguration loggerConfig,
        LoggerSinkRegistry sinkRegistry,
        LoggerEnricherRegistry enricherRegistry,
        LogSinkProviderRegistry? nativeSinkRegistry = null)
    {
        _loggerConfig      = loggerConfig;
        _sinkRegistry      = sinkRegistry;
        _enricherRegistry  = enricherRegistry;
        _nativeSinkRegistry = nativeSinkRegistry ?? LogSinkProviderRegistry.Default;
    }

    // ── Entry point ───────────────────────────────────────────────────────────

    /// <summary>
    /// Apply the <c>Serilog</c> section of <paramref name="configuration"/> to
    /// <see cref="_loggerConfig"/>. A missing <c>Serilog</c> key is a no-op.
    /// </summary>
    internal void Apply(IConfiguration configuration)
    {
        var section = configuration.GetSection("Serilog");
        if (!section.Exists()) return;

        ReadMinimumLevel(section);
        ReadWriteTo(section);
        ReadEnrich(section);
        // "Using" is intentionally skipped — see class-level XML doc.
    }

    // ── MinimumLevel ──────────────────────────────────────────────────────────

    // Supports both:
    //   "MinimumLevel": "Warning"                 (string shorthand)
    //   "MinimumLevel": { "Default": "Debug", "Override": { ... } }  (object form)
    private void ReadMinimumLevel(IConfigurationSection serilogSection)
    {
        var minLevelSection = serilogSection.GetSection("MinimumLevel");
        if (!minLevelSection.Exists()) return;

        // String shorthand — the section's .Value is non-null when it's a leaf.
        if (minLevelSection.Value is { } levelName)
        {
            ApplyDefaultLevel(levelName);
            return;
        }

        // Object form — "Default" + optional "Override" block.
        var defaultValue = minLevelSection["Default"];
        if (defaultValue is not null)
            ApplyDefaultLevel(defaultValue);

        var overrideSection = minLevelSection.GetSection("Override");
        foreach (var child in overrideSection.GetChildren())
        {
            // Dotted keys ("Microsoft.Extensions") come through naturally because
            // IConfiguration treats the entire key as a single flat string here —
            // the Override section's children are keyed by namespace prefix.
            var overrideLevel = child.Value ?? "Information";
            ApplyOverride(child.Key, overrideLevel);
        }
    }

    // Set the pipeline default minimum level.
    // C# 12 note: kept this form for .NET 9/10 compatibility.
    // The switch expression is readable here; no simplification needed on C# 14.
    private void ApplyDefaultLevel(string levelName)
        => _loggerConfig.MinimumLevel.Is(ParseLevel(levelName));

    // Add a per-source-context (namespace) override.
    // Dotted keys work naturally through MinimumLevelConfiguration.Override(string, LogEventLevel).
    private void ApplyOverride(string source, string levelName)
        => _loggerConfig.MinimumLevel.Override(source, ParseLevel(levelName));

    // ── WriteTo ───────────────────────────────────────────────────────────────

    // Each entry is either:
    //   { "Name": "Console" }
    //   { "Name": "File", "Args": { "path": "log.txt" } }
    // Unresolved names THROW SinkResolutionException (Risk 2 contract).
    private void ReadWriteTo(IConfigurationSection serilogSection)
    {
        var writeToSection = serilogSection.GetSection("WriteTo");
        foreach (var entry in writeToSection.GetChildren())
        {
            // Support both { "Name": "X" } and flat string shorthand "X"
            var name = entry["Name"] ?? entry.Value;

            if (string.IsNullOrWhiteSpace(name))
                throw new SinkResolutionException("(null)",
                    "WriteTo entry has no 'Name' field and no shorthand string value.");

            var argsSection = entry.GetSection("Args");

            // Stage 1: Layer-1 fluent verb (Console, File, Http, ...).
            if (_sinkRegistry.TryResolve(name, out var factory))
            {
                factory!(_loggerConfig, argsSection);
                continue;
            }

            // Stage 2: registry bridge — fall through to the native engine
            // registry by SinkKind (case-insensitive). This is what makes the
            // MMP.Herald.Sinks.* packages addressable by Serilog Name.
            if (_nativeSinkRegistry.Contains(name))
            {
                ApplyNativeSink(name, argsSection);
                continue;
            }

            // Resolved by neither registry — hard error (Risk 2 preserved).
            throw new SinkResolutionException(name, UnresolvedReason(name));
        }
    }

    // Build the explanatory reason for a name that resolved in neither registry.
    // Kept separate so ReadWriteTo stays flat (guard-clause + continue) and the
    // community-NuGet messaging lives in one place.
    private static string UnresolvedReason(string name)
        => IsLikelyCommunityNuGetSink(name)
            ? "This appears to be a community NuGet sink. The Herald shim cannot " +
              "load it — see the parity audit for the identity-wall explanation."
            : "No matching sink found in the Layer-1 registry or the native " +
              "LogSinkProviderRegistry. Register a Layer-1 factory via " +
              "LoggerSinkRegistry.RegisterSink(), or add the MMP.Herald.Sinks.* " +
              "package that provides the '" + name + "' sink kind.";

    // Declare a native sink kind into the builder's JSON config via WithNetworkSink.
    // The engine resolves the provider from the native registry at build time — the
    // reader never calls CreateSink directly, so JSON stays the single source of
    // truth for pipeline construction. WithNetworkSink requires a non-blank endpoint
    // (it lands in the sink definition's Uri slot), so a native sink declared this
    // way must carry one of the EndpointArgKeys in its Args.
    private void ApplyNativeSink(string kind, IConfigurationSection argsSection)
    {
        var endpoint = ResolveEndpoint(argsSection);
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new SinkResolutionException(kind,
                "this native sink kind requires a push endpoint, but the WriteTo " +
                "Args carried none. Supply one of: " + string.Join(", ", EndpointArgKeys) + ".");

        // Route through the public Layer-1 verb (WriteTo.Native), which forwards to
        // QuickLogBuilder.WithNetworkSink. Going through the fluent surface keeps the
        // compat layer off the engine's internal Builder property — the bridge stays
        // composed of public seams only.
        _loggerConfig.WriteTo.Native(kind, endpoint!);
    }

    // First non-blank value among the accepted endpoint arg keys (direct or nested
    // under "Args:"), or null. Iterating the named list keeps the accepted aliases
    // in one place and the control flow flat.
    private static string? ResolveEndpoint(IConfigurationSection argsSection)
    {
        foreach (var key in EndpointArgKeys)
        {
            var value = argsSection[key] ?? argsSection["Args:" + key];
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    // ── Cross-registry resolution query (standing conformance contract) ───────

    /// <summary>
    /// True when <paramref name="name"/> resolves to a sink via either the Layer-1
    /// registry or the native registry bridge. Exposed so the conformance regression
    /// test can pin cross-registry resolution without driving a full Apply.
    /// </summary>
    internal bool CanResolveSinkName(string name)
        => _sinkRegistry.IsRegistered(name) || _nativeSinkRegistry.Contains(name);

    // Heuristic: dotted names or well-known community packages hint at a NuGet sink.
    // This is best-effort messaging only — the throw happens regardless.
    private static bool IsLikelyCommunityNuGetSink(string name)
        => name.Contains('.') || name.Contains("Seq", StringComparison.OrdinalIgnoreCase)
        || name.Contains("MSSql", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Datadog", StringComparison.OrdinalIgnoreCase);

    // ── Enrich ────────────────────────────────────────────────────────────────

    // Supports the most common production pattern (Risk 10):
    //   "Enrich": ["FromLogContext", "WithProperty"]   (string-array shorthand)
    // Also supports the object form:
    //   "Enrich": [{ "Name": "WithProperty", "Args": { "name": "Env", "value": "prod" } }]
    //
    // Unresolved enricher names are silently skipped — enrichers are decorative and
    // a missing one should not prevent log delivery (asymmetric with sinks).
    private void ReadEnrich(IConfigurationSection serilogSection)
    {
        var enrichSection = serilogSection.GetSection("Enrich");
        foreach (var child in enrichSection.GetChildren())
        {
            // String-array element: child.Value is "FromLogContext" etc.
            // Object element: child["Name"] carries the name.
            var name = child.Value ?? child["Name"];

            if (string.IsNullOrWhiteSpace(name)) continue;

            if (_enricherRegistry.TryResolve(name, out var factory))
                factory!(_loggerConfig, child.GetSection("Args"));
            // Unresolved enricher — intentional skip (see method comment).
        }
    }

    // ── Shared level parser ───────────────────────────────────────────────────

    // Case-insensitive; falls back to Information for unknown strings so that
    // a misconfigured Override key degrades gracefully (enricher-like policy —
    // a bad override level should not crash startup).
    // Default-level parsing uses ApplyDefaultLevel → ParseLevel, which throws
    // ArgumentException for unknown names via the dedicated overload above.
    private static LogEventLevel ParseLevel(string name) => name.ToLowerInvariant() switch
    {
        "verbose"     => LogEventLevel.Verbose,
        "debug"       => LogEventLevel.Debug,
        "information" => LogEventLevel.Information,
        "warning"     => LogEventLevel.Warning,
        "error"       => LogEventLevel.Error,
        "fatal"       => LogEventLevel.Fatal,
        _             => throw new ArgumentException(
                             $"Unknown log level '{name}'. Valid values: " +
                             "Verbose, Debug, Information, Warning, Error, Fatal.",
                             nameof(name))
    };
}
