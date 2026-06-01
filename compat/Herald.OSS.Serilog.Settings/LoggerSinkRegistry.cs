// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

#nullable enable

using System;
using Microsoft.Extensions.Configuration;
using MMP.Herald.Output.Rendering.Themes;
using MMP.Herald.Serilog.Configuration;

namespace Herald.OSS.Serilog.Settings;

/// <summary>
/// Maps appsettings.json <c>WriteTo</c> sink names to Herald builder lambdas.
///
/// <para>
/// <b>Usage</b>: the parser (Task 3) looks up each <c>Name</c> entry in
/// <see cref="Default"/> (or a caller-supplied instance) and invokes the
/// registered factory with the section that holds the sink's <c>Args</c>.
/// </para>
///
/// <para>
/// <b>Built-in names</b> mirror the <see cref="LoggerSinkConfiguration"/> verbs
/// available in Layer-1: Console, File, Http, TCPSink, UDPSink, Elasticsearch,
/// OpenTelemetry, OpenTelemetryProtobuf, Null.
/// </para>
///
/// <para>
/// <b>Collision policy</b>: <see cref="RegisterSink"/> throws
/// <see cref="InvalidOperationException"/> on duplicate names rather than
/// silently overwriting — prevents accidental shadowing of built-ins and
/// makes misconfiguration loud. (Pre-mortem Risk 2.)
/// </para>
///
/// <para>
/// <b>Null/blank guard</b>: <see cref="IsRegistered"/> always returns
/// <c>false</c> for null, empty, or whitespace-only names. (Pre-mortem Risk 1.)
/// </para>
///
/// <para>
/// <b>Default singleton</b>: <see cref="Default"/> is sealed at construction —
/// calling <see cref="RegisterSink"/> on it throws. Obtain a mutable copy via
/// <see cref="CreateDefault"/> and pass it to the parser/builder.
/// </para>
/// </summary>
public sealed class LoggerSinkRegistry
{
    // ── Factory signature ─────────────────────────────────────────────────────

    /// <summary>
    /// A factory that applies a single sink entry to a <see cref="LoggerConfiguration"/>
    /// and returns the same instance for fluent chaining.
    /// </summary>
    /// <param name="builder">The configuration root to mutate.</param>
    /// <param name="sinkArgs">
    ///   The <see cref="IConfigurationSection"/> for this WriteTo entry — contains
    ///   any <c>Args</c> sub-keys (e.g. <c>path</c>, <c>requestUri</c>).
    /// </param>
    public delegate LoggerConfiguration SinkFactory(
        LoggerConfiguration builder,
        IConfigurationSection sinkArgs);

    // ── Internal state ────────────────────────────────────────────────────────

    private readonly NamedFactoryRegistry<SinkFactory> _inner;

    private LoggerSinkRegistry(NamedFactoryRegistry<SinkFactory> inner) => _inner = inner;

    // ── Singleton ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The sealed, pre-seeded registry used by the parser when no custom instance
    /// is provided. Cannot be mutated — use <see cref="CreateDefault"/> for a copy
    /// that accepts additional registrations.
    /// </summary>
    public static LoggerSinkRegistry Default { get; } = BuildSealed();

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Create a fresh, mutable registry pre-seeded with all Herald built-in sinks.
    /// Safe to extend with application-specific or library sinks.
    /// </summary>
    public static LoggerSinkRegistry CreateDefault()
    {
        var registry = new NamedFactoryRegistry<SinkFactory>();
        Seed(registry);
        // Not sealed — callers may call RegisterSink.
        return new LoggerSinkRegistry(registry);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Register a custom sink factory under <paramref name="name"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///   <paramref name="name"/> is already registered, or this registry is sealed
    ///   (i.e. it is <see cref="Default"/>).
    /// </exception>
    public void RegisterSink(string name, SinkFactory factory)
        => _inner.Register(name, factory);

    /// <summary>
    /// Returns <c>true</c> when a factory is registered under <paramref name="name"/>
    /// (case-insensitive). Always returns <c>false</c> for null, empty, or whitespace.
    /// </summary>
    public bool IsRegistered(string? name) => _inner.Contains(name);

    /// <summary>
    /// Attempt to resolve the factory for <paramref name="name"/>.
    /// Returns <c>false</c> and sets <paramref name="factory"/> to null when the
    /// name is unknown — the caller (Task 3 parser) must then throw, not log.
    /// (Pre-mortem Risk 2.)
    /// </summary>
    public bool TryResolve(string name, out SinkFactory? factory)
        => _inner.TryGet(name, out factory);

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Seed all Layer-1 built-in sink names into <paramref name="registry"/>.
    /// Factory bodies are kept intentionally minimal — they delegate all
    /// parameter extraction to <see cref="LoggerSinkConfiguration"/> verbs.
    /// </summary>
    private static void Seed(NamedFactoryRegistry<SinkFactory> registry)
    {
        // Console — optional "theme" arg (W4 second entry point). A named theme
        // routes through the themed overload; absent/blank/"none" keeps the plain
        // console verb so a config without a theme is byte-identical to before.
        registry.Register("Console", static (b, args) =>
        {
            var themeName = args["theme"] ?? args["Args:theme"];
            var theme = ResolveConsoleTheme(themeName);
            if (theme is null)
                b.WriteTo.Console();
            else
                b.WriteTo.Console(theme);
            return b;
        });

        // File — "path" key required; fall back to a safe default so the
        // factory is never null even with a missing arg.
        registry.Register("File", static (b, args) =>
        {
            var path = args["path"] ?? args["Args:path"] ?? "log.txt";
            b.WriteTo.File(path);
            return b;
        });

        // Http — "requestUri" key required; empty string is a caller config error
        // that the parser (Task 3) will surface before reaching here.
        registry.Register("Http", static (b, args) =>
        {
            var uri = args["requestUri"] ?? args["Args:requestUri"] ?? string.Empty;
            b.WriteTo.Http(uri);
            return b;
        });

        // TCP — host + port.
        registry.Register("TCPSink", static (b, args) =>
        {
            var host = args["host"] ?? args["Args:host"] ?? "localhost";
            var port = int.TryParse(args["port"] ?? args["Args:port"], out var p) ? p : 514;
            b.WriteTo.TCPSink(host, port);
            return b;
        });

        // UDP — host + port.
        registry.Register("UDPSink", static (b, args) =>
        {
            var host = args["host"] ?? args["Args:host"] ?? "localhost";
            var port = int.TryParse(args["port"] ?? args["Args:port"], out var p) ? p : 514;
            b.WriteTo.UDPSink(host, port);
            return b;
        });

        // Elasticsearch — clusterUrl.
        registry.Register("Elasticsearch", static (b, args) =>
        {
            var url = args["clusterUrl"] ?? args["Args:clusterUrl"] ?? string.Empty;
            b.WriteTo.Elasticsearch(url);
            return b;
        });

        // OpenTelemetry (JSON) — endpoint.
        registry.Register("OpenTelemetry", static (b, args) =>
        {
            var endpoint = args["endpoint"] ?? args["Args:endpoint"] ?? string.Empty;
            b.WriteTo.OpenTelemetry(endpoint);
            return b;
        });

        // OpenTelemetry Protobuf — endpoint.
        registry.Register("OpenTelemetryProtobuf", static (b, args) =>
        {
            var endpoint = args["endpoint"] ?? args["Args:endpoint"] ?? string.Empty;
            b.WriteTo.OpenTelemetryProtobuf(endpoint);
            return b;
        });

        // Null — drops every event; no args.
        registry.Register("Null", static (b, _) =>
        {
            b.WriteTo.Null();
            return b;
        });
    }

    // Map an appsettings "theme" arg to a ConsoleTheme member, or null for the
    // plain (unthemed) console. Accepts both the bare Herald name ("Literate") and
    // the Serilog-qualified form ("AnsiConsoleTheme.Literate" / "ConsoleTheme.Code")
    // by matching on the final segment, case-insensitively. Unknown names fall back
    // to the plain console (a bad theme name must not break log delivery — the same
    // asymmetry the reader applies to enrichers).
    private static IConsoleTheme? ResolveConsoleTheme(string? themeName)
    {
        if (string.IsNullOrWhiteSpace(themeName)) return null;

        var lastDot = themeName!.LastIndexOf('.');
        var leaf = lastDot >= 0 ? themeName.Substring(lastDot + 1) : themeName;

        return leaf.Trim().ToLowerInvariant() switch
        {
            "literate" => ConsoleTheme.Literate,
            "code" or "colored" or "coloured" => ConsoleTheme.Code,
            "grayscale" or "greyscale" => ConsoleTheme.Grayscale,
            "none" => null,   // explicit "none" == plain console (byte-identical to unthemed)
            _ => null,        // unknown theme name → plain console (do not break delivery)
        };
    }

    /// <summary>
    /// Build and seal the <see cref="Default"/> singleton.
    /// </summary>
    private static LoggerSinkRegistry BuildSealed()
    {
        var registry = new NamedFactoryRegistry<SinkFactory>();
        Seed(registry);
        registry.Seal();
        return new LoggerSinkRegistry(registry);
    }
}
