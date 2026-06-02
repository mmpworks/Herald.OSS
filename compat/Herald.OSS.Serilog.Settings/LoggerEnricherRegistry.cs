// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

#nullable enable

using System;
using Microsoft.Extensions.Configuration;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Configuration;

namespace Herald.OSS.Serilog.Settings;

/// <summary>
/// Maps appsettings.json <c>Enrich</c> enricher names to Herald builder lambdas.
///
/// <para>
/// <b>Built-in names</b> mirror the <see cref="LoggerEnrichmentConfiguration"/>
/// verbs available in Layer-1: <c>FromLogContext</c>, <c>WithProperty</c>.
/// </para>
///
/// <para>
/// Follows the same contract as <see cref="LoggerSinkRegistry"/>:
/// collision-throw policy, case-insensitive lookup, null/blank guard,
/// and a sealed <see cref="Default"/> singleton alongside a mutable
/// <see cref="CreateDefault"/> factory.
/// </para>
/// </summary>
public sealed class LoggerEnricherRegistry
{
    // ── Factory signature ─────────────────────────────────────────────────────

    /// <summary>
    /// A factory that applies a single enricher entry to a
    /// <see cref="LoggerConfiguration"/> and returns the same instance for
    /// fluent chaining.
    /// </summary>
    /// <param name="builder">The configuration root to mutate.</param>
    /// <param name="enricherArgs">
    ///   The <see cref="IConfigurationSection"/> for this Enrich entry — contains
    ///   any <c>Args</c> sub-keys (e.g. <c>name</c>, <c>value</c>).
    /// </param>
    public delegate LoggerConfiguration EnricherFactory(
        LoggerConfiguration builder,
        IConfigurationSection enricherArgs);

    // ── Internal state ────────────────────────────────────────────────────────

    private readonly NamedFactoryRegistry<EnricherFactory> _inner;

    private LoggerEnricherRegistry(NamedFactoryRegistry<EnricherFactory> inner) => _inner = inner;

    // ── Singleton ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The sealed, pre-seeded registry used by the parser when no custom instance
    /// is provided. Cannot be mutated — use <see cref="CreateDefault"/> for a copy
    /// that accepts additional registrations.
    /// </summary>
    public static LoggerEnricherRegistry Default { get; } = BuildSealed();

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Create a fresh, mutable registry pre-seeded with all Herald built-in enrichers.
    /// Safe to extend with application-specific enrichers.
    /// </summary>
    public static LoggerEnricherRegistry CreateDefault()
    {
        var registry = new NamedFactoryRegistry<EnricherFactory>();
        Seed(registry);
        // Not sealed — callers may call RegisterEnricher.
        return new LoggerEnricherRegistry(registry);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Register a custom enricher factory under <paramref name="name"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///   <paramref name="name"/> is already registered, or this registry is sealed
    ///   (i.e. it is <see cref="Default"/>).
    /// </exception>
    public void RegisterEnricher(string name, EnricherFactory factory)
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
    /// </summary>
    public bool TryResolve(string name, out EnricherFactory? factory)
        => _inner.TryGet(name, out factory);

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Seed all Layer-1 built-in enricher names into <paramref name="registry"/>.
    /// </summary>
    private static void Seed(NamedFactoryRegistry<EnricherFactory> registry)
    {
        // FromLogContext — no args; maps to the Layer-1 no-op (async-local scope
        // is already wired at build time in P1).
        registry.Register("FromLogContext", static (b, _) =>
        {
            b.Enrich.FromLogContext();
            return b;
        });

        // WithProperty — name + value args.
        // value is stored as a raw string; the enrichment path stores object?.
        registry.Register("WithProperty", static (b, args) =>
        {
            var name  = args["name"]  ?? args["Args:name"]  ?? string.Empty;
            var value = args["value"] ?? args["Args:value"];
            b.Enrich.WithProperty(name, value);
            return b;
        });
    }

    /// <summary>
    /// Build and seal the <see cref="Default"/> singleton.
    /// </summary>
    private static LoggerEnricherRegistry BuildSealed()
    {
        var registry = new NamedFactoryRegistry<EnricherFactory>();
        Seed(registry);
        registry.Seal();
        return new LoggerEnricherRegistry(registry);
    }
}
