#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Routing;

namespace MMP.Herald.Quick;

/// <summary>
/// Manages sink providers visible to a QuickLogBuilder. Keyed by SinkKind.
/// Supports optional protection (survives Reset).
///
/// <para>
/// Two sources are consulted when resolving by kind:
/// </para>
/// <list type="number">
/// <item>The local set — providers explicitly added to this builder via
/// <see cref="Add"/>, typically through <c>WithCustomSinkProvider</c>.</item>
/// <item>The fallback registry — <see cref="LogSinkProviderRegistry.Default"/>
/// in production. Auto-populated at assembly load by every
/// <c>Herald.Sinks.*</c> package via the <c>[ModuleInitializer]</c> emitted
/// by <c>MMP.Herald.Generators.SinkAutoRegistrationGenerator</c>. This is
/// what makes <c>dotnet add package MMP.Herald.Sinks.Trace</c> turn into
/// "Trace just works" without a manual <see cref="Add"/> call.</item>
/// </list>
/// <para>
/// Local entries take precedence over fallback entries on collision —
/// callers who explicitly add a provider override the auto-registered one.
/// <see cref="Count"/> reports the local set only (the fallback is process-
/// wide and not "this builder's count").
/// </para>
/// </summary>
public sealed class SinkProviderSet
{
    private readonly QuickLogBuilder _builder;
    private LogSinkProviderRegistry _fallbackRegistry;
    internal readonly List<NamedRegistration<ILogSinkProvider>> Items = new();

    internal SinkProviderSet(QuickLogBuilder builder)
    {
        _builder = builder;
        _fallbackRegistry = LogSinkProviderRegistry.Default;
    }

    /// <summary>
    /// Test seam: replace the fallback registry with an isolated instance.
    /// Production code never calls this — the <see cref="LogSinkProviderRegistry.Default"/>
    /// singleton is the intended source of auto-registered providers. Tests
    /// pass an isolated registry to avoid cross-test contamination through
    /// the process-wide Default.
    /// </summary>
    internal void SetFallbackRegistry(LogSinkProviderRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _fallbackRegistry = registry;
    }

    /// <summary>
    /// The fallback sink-provider registry this builder resolves through —
    /// <see cref="LogSinkProviderRegistry.Default"/> in production, an isolated
    /// instance under <see cref="SetFallbackRegistry"/> in tests. Threaded into
    /// the build as the sink-registry seed so native sink kinds declared here
    /// (e.g. via <c>WriteTo.Native</c> / <c>WithNetworkSink</c>) reach
    /// <c>ILogSinkProvider.CreateSink</c> at build time (G1.3).
    /// </summary>
    internal LogSinkProviderRegistry FallbackRegistry => _fallbackRegistry;

    // -- Add --

    /// <summary>Add a sink provider (keyed by its SinkKind).</summary>
    public QuickLogBuilder Add(ILogSinkProvider provider, bool protect = false) {
        Items.Add(new NamedRegistration<ILogSinkProvider>(provider.SinkKind, provider, protect));
        return _builder;
    }

    // -- Remove --

    /// <summary>Remove a sink provider by sink kind. Removes from the local
    /// set only; the fallback registry is process-wide and not mutated.</summary>
    public QuickLogBuilder Remove(string sinkKind) {
        Items.RemoveAll(r => string.Equals(r.Name, sinkKind, StringComparison.OrdinalIgnoreCase));
        return _builder;
    }

    /// <summary>Remove all non-protected local sink providers. The fallback
    /// registry is process-wide and not mutated.</summary>
    public QuickLogBuilder Clear() {
        Items.RemoveAll(static r => !r.IsProtected);
        return _builder;
    }

    // -- Query --

    /// <summary>Get a sink provider by sink kind. Checks the local set
    /// first, then falls back to the auto-registration registry
    /// (<see cref="LogSinkProviderRegistry.Default"/> in production).
    /// Local entries override fallback entries on collision.</summary>
    public ILogSinkProvider? Get(string sinkKind)
    {
        var local = NamedItemQuery.Get<ILogSinkProvider, ILogSinkProvider>(Items, sinkKind);
        if (local is not null) return local;
        return _fallbackRegistry.Contains(sinkKind)
            ? _fallbackRegistry.Resolve(sinkKind)
            : null;
    }

    /// <summary>True when the kind is resolvable either locally or via the
    /// fallback registry. Mirrors <see cref="Get"/>'s precedence.</summary>
    public bool Has(string sinkKind) =>
        NamedItemQuery.Has(Items, sinkKind) ||
        _fallbackRegistry.Contains(sinkKind);

    /// <summary>Current local provider count. Does NOT include providers
    /// surfaced through the fallback registry — those are process-wide and
    /// not "this builder's count."</summary>
    public int Count => Items.Count;
}
