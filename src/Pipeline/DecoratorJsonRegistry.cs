#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Quick;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Reconstructs <see cref="IConfigurablePipelineDecorator"/> instances from
/// <see cref="JsonPipelineDecoratorConfig"/>. Custom decorators live in
/// plugins, so the registry ships with no built-ins; plugin authors
/// register their factories at init time alongside the existing
/// <c>builder.WithPipelineDecorator(...)</c> wiring.
///
/// <para>
/// Mirrors <c>EnricherJsonRegistry</c>. The split exists for the same
/// reason: deserialization needs a string → type lookup that can't live on
/// the decorator instance itself, because the runtime has to construct an
/// instance from JSON without one to call <c>ToJsonConfig()</c> on.
/// </para>
///
/// <para>
/// <b>Hosting model.</b> Forwards every call to
/// <see cref="HeraldHost.Default"/>'s <see cref="PipelineDecoratorKindRegistry"/>.
/// Tests and multi-tenant hosts that want isolated registry state
/// construct their own <c>HeraldHost</c> and consume <c>host.Decorators</c>.
/// </para>
///
/// <para>
/// Unknown kinds throw with an actionable message. Silent fallback was
/// considered and rejected — a Lean deploy that loads JSON and quietly
/// drops a custom decorator on every Reload is exactly the failure mode
/// the JSON-lossless refactor was opened to close.
/// </para>
/// </summary>
public static class DecoratorJsonRegistry
{
    /// <summary>
    /// Register a factory for a custom decorator kind. Plugin authors call
    /// this once at plugin init. Last registration wins; the registry is
    /// process-wide, so the same kind across plugins resolves to whichever
    /// plugin loaded last.
    /// </summary>
    public static void Register(
        string kind,
        Func<IReadOnlyDictionary<string, object?>?, IConfigurablePipelineDecorator> factory) =>
        HeraldHost.Default.Decorators.Register(kind, factory);

    /// <summary>
    /// Reconstruct a decorator from its JSON config. Throws for unknown
    /// kinds, with a message pointing the caller at the registration step
    /// the plugin missed.
    /// </summary>
    public static IConfigurablePipelineDecorator Reconstruct(JsonPipelineDecoratorConfig config) =>
        HeraldHost.Default.Decorators.Reconstruct(config);

    /// <summary>
    /// True if the registry knows how to reconstruct the given kind. Useful
    /// for validation paths that want to surface an early error rather than
    /// throw deep inside <see cref="Reconstruct"/>.
    /// </summary>
    public static bool IsRegistered(string kind) =>
        HeraldHost.Default.Decorators.IsRegistered(kind);

    /// <summary>
    /// Remove a registration. Primarily useful for tests that want to
    /// exercise the throw-on-unknown-kind path without tearing down the
    /// process. Returns true if the kind was registered.
    /// </summary>
    public static bool Unregister(string kind) =>
        HeraldHost.Default.Decorators.Unregister(kind);
}
