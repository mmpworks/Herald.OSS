#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Quick;

namespace MMP.Herald.Enrichers;

/// <summary>
/// Reconstructs <see cref="ILogEnricher"/> instances from
/// <see cref="JsonEnricherConfig"/>. Built-in enricher kinds are registered
/// at type init; plugin authors add their own via
/// <see cref="Register(string, Func{IReadOnlyDictionary{string, object?}?, ILogEnricher})"/>.
///
/// <para>
/// This is the deserialization side of the JSON-as-source-of-truth contract:
/// <see cref="ILogEnricher.ToJsonConfig"/> writes the JSON, this registry
/// reads it back. The split exists because deserialization needs a string →
/// type lookup that can't live on the enricher itself (you can't call a
/// virtual method on a type you haven't constructed yet).
/// </para>
///
/// <para>
/// <b>Hosting model.</b> The static surface forwards every call to
/// <see cref="HeraldHost.Default"/>'s <see cref="EnricherKindRegistry"/>.
/// Tests and multi-tenant hosts that want isolated registry state
/// construct their own <c>HeraldHost</c> and consume
/// <c>host.Enrichers</c> directly.
/// </para>
///
/// <para>
/// Unknown kinds throw with an actionable message. Silent fallback was
/// considered and rejected — a plugin pipeline that loses its enricher on
/// rebuild and quietly drops events is exactly the failure mode that
/// motivated this refactor.
/// </para>
/// </summary>
public static class EnricherJsonRegistry
{
    /// <summary>
    /// Register a factory for a custom enricher kind. Plugin authors call
    /// this once at plugin init. Last registration wins; the registry is
    /// process-wide so the same kind name across plugins is a collision the
    /// last loaded plugin resolves.
    /// </summary>
    public static void Register(
        string kind,
        Func<IReadOnlyDictionary<string, object?>?, ILogEnricher> factory) =>
        HeraldHost.Default.Enrichers.Register(kind, factory);

    /// <summary>
    /// Reconstruct an enricher from its JSON config. Throws for unknown kinds.
    /// </summary>
    public static ILogEnricher Reconstruct(JsonEnricherConfig config) =>
        HeraldHost.Default.Enrichers.Reconstruct(config);

    /// <summary>
    /// True if the registry knows how to reconstruct the given kind. Useful
    /// for validation paths that want to surface an early error rather than
    /// throw deep inside <see cref="Reconstruct"/>.
    /// </summary>
    public static bool IsRegistered(string kind) =>
        HeraldHost.Default.Enrichers.IsRegistered(kind);

    /// <summary>
    /// Remove a registration. Primarily for tests that need to exercise the
    /// throw-on-unknown-kind path without tearing down the process. Mirrors
    /// the same shape on <see cref="Pipeline.DecoratorJsonRegistry"/>.
    /// Returns true if the kind was registered.
    /// </summary>
    public static bool Unregister(string kind) =>
        HeraldHost.Default.Enrichers.Unregister(kind);
}
