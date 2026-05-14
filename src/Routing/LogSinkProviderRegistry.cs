#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace MMP.Herald.Routing;

/// <summary>
/// Registry of sink providers keyed by sink kind.
/// Registration happens at bootstrap; after that, the registry is read-only
/// in practice.
///
/// <para>
/// Backed by <see cref="ConcurrentDictionary{TKey,TValue}"/> so two plugin
/// bootstraps racing on <see cref="Register"/> cannot corrupt the bucket
/// chain. Indexer write and <see cref="ConcurrentDictionary{TKey,TValue}.TryGetValue"/>
/// are both thread-safe, so reads stay lock-free.
/// </para>
///
/// <para>
/// <b>Default registry.</b> <see cref="Default"/> is a process-wide singleton
/// every Herald.Sinks.* package auto-populates via <c>[ModuleInitializer]</c>
/// at assembly load. Consumers can either rely on it ("<c>dotnet add package
/// MMP.Herald.Sinks.X</c> is the whole workflow") or construct their own
/// isolated <see cref="LogSinkProviderRegistry"/> for hosts that need
/// strict per-tenant separation. The <c>[ModuleInitializer]</c> path is
/// emitted by <c>MMP.Herald.Generators.SinkAutoRegistrationGenerator</c>;
/// see <c>docs/sink-auto-registration.md</c> for the full story.
/// </para>
/// </summary>
public sealed class LogSinkProviderRegistry
{
    private readonly ConcurrentDictionary<string, ILogSinkProvider> _providers =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Process-wide default registry. Auto-populated by every
    /// <c>Herald.Sinks.*</c> package at assembly load. Hosts can resolve
    /// sinks from here ("works out of the box") or instantiate their own
    /// <see cref="LogSinkProviderRegistry"/> and call <see cref="Register"/>
    /// manually for stricter isolation.
    /// </summary>
    public static LogSinkProviderRegistry Default { get; } = new();

    public void Register(ILogSinkProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers[provider.SinkKind.ToLowerInvariant()] = provider;
    }

    public ILogSinkProvider Resolve(string sinkKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sinkKind);

        if (_providers.TryGetValue(sinkKind, out var provider))
        {
            return provider;
        }

        throw new KeyNotFoundException(
            $"No sink provider registered for kind '{sinkKind}'. " +
            $"Available kinds: {string.Join(", ", _providers.Keys)}.");
    }

    /// <summary>
    /// True when a provider is registered for <paramref name="sinkKind"/>.
    /// Case-insensitive lookup.
    /// </summary>
    public bool Contains(string sinkKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sinkKind);
        return _providers.ContainsKey(sinkKind);
    }

    /// <summary>
    /// Snapshot of every registered provider. Order is not stable across calls.
    /// Useful for diagnostics and for code that wants to enumerate all sinks
    /// that auto-registered at assembly load.
    /// </summary>
    public IReadOnlyCollection<ILogSinkProvider> AllProviders() =>
        (IReadOnlyCollection<ILogSinkProvider>)_providers.Values;

    /// <summary>
    /// Snapshot of every registered sink kind. Case is preserved as the
    /// provider's <see cref="ILogSinkProvider.SinkKind"/> reported it
    /// (registry stores lower-case keys, but provider instances retain
    /// their declared casing).
    /// </summary>
    public IReadOnlyCollection<string> AllKinds()
    {
        var keys = new List<string>(_providers.Count);
        foreach (var p in _providers.Values) keys.Add(p.SinkKind);
        return keys;
    }
}
