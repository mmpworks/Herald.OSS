#nullable enable

using System;
using System.Collections.Concurrent;

namespace MMP.Herald.Routing;

/// <summary>
/// Process-wide registry of <see cref="ISinkWrapperFactory"/> instances
/// keyed by Kind name. Plugin assemblies register their wrapper factories
/// at module-init time (e.g., Herald.Pro's bootstrap registers the "retry"
/// factory; Herald.Enterprise's bootstrap registers "audit"). Core's
/// DefaultLogSinkFactory queries by kind name to wrap a sink without
/// any compile-time dependency on plugin types.
///
/// <para>
/// Thread-safe: backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// so concurrent plugin module initializers don't race. Register is
/// last-writer-wins by design — a plugin that wants to override a
/// built-in wrapper kind can do so.
/// </para>
/// </summary>
public static class SinkWrapperRegistry
{
    private static readonly ConcurrentDictionary<string, ISinkWrapperFactory> _factories =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Register a wrapper factory. Replaces any existing entry for the same
    /// <see cref="ISinkWrapperFactory.Kind"/>.
    /// </summary>
    public static void Register(ISinkWrapperFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentException.ThrowIfNullOrWhiteSpace(factory.Kind);
        _factories[factory.Kind] = factory;
    }

    /// <summary>
    /// Look up a wrapper factory by kind. Returns null if no factory is
    /// registered — the caller decides whether that's a hard error
    /// (config asked for a wrap but the plugin isn't loaded) or a no-op
    /// (config didn't ask for a wrap).
    /// </summary>
    public static ISinkWrapperFactory? Resolve(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return null;
        return _factories.TryGetValue(kind, out var factory) ? factory : null;
    }
}
