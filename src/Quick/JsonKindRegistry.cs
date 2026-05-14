#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace MMP.Herald.Quick;

/// <summary>
/// Shared base for the JSON-kind registries that map a string kind to a
/// factory producing <typeparamref name="TInstance"/>. Both
/// <see cref="MMP.Herald.Enrichers.EnricherKindRegistry"/> and
/// <see cref="MMP.Herald.Pipeline.PipelineDecoratorKindRegistry"/> follow
/// this shape: a process-wide concurrent dictionary keyed
/// case-insensitively, an unknown-kind throw with an actionable message,
/// and a test-friendly <c>Unregister</c>.
///
/// <para>
/// The kind-message shape is the only thing that legitimately differs
/// between the two — different built-ins, different registration entry
/// points. Subclasses override <see cref="BuildUnknownKindMessage"/> and
/// register their built-ins in their constructors. Reconstruct, Register,
/// IsRegistered, and Unregister live here once.
/// </para>
///
/// <para>
/// Inheritance is part of the design here — the base captures the shared
/// surface, so it stays unsealed; concrete leaves are sealed.
/// </para>
/// </summary>
public abstract class JsonKindRegistry<TInstance> where TInstance : class
{
    // OrdinalIgnoreCase keeps every JSON-kind registry consistent — a
    // lowercase kind resolves the same as its canonical-case sibling.
    private readonly ConcurrentDictionary<string, Func<IReadOnlyDictionary<string, object?>?, TInstance>> _factories =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Register a factory for the given kind. Plugin authors call this once
    /// at plugin init. Last registration wins; the registry is process-wide
    /// for the host that owns it (the default host backs the static
    /// facades).
    /// </summary>
    public void Register(
        string kind,
        Func<IReadOnlyDictionary<string, object?>?, TInstance> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(factory);
        _factories[kind] = factory;
    }

    /// <summary>
    /// Reconstruct an instance from <paramref name="kind"/> +
    /// <paramref name="properties"/>. Throws when no factory is registered
    /// for the kind; subclasses control the message via
    /// <see cref="BuildUnknownKindMessage"/>.
    /// </summary>
    public TInstance Reconstruct(string kind, IReadOnlyDictionary<string, object?>? properties)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (!_factories.TryGetValue(kind, out var factory))
        {
            throw new InvalidOperationException(BuildUnknownKindMessage(kind));
        }
        return factory(properties);
    }

    /// <summary>
    /// True when a factory is registered for <paramref name="kind"/>.
    /// Lets validation paths surface an early error instead of throwing
    /// deep inside <see cref="Reconstruct"/>.
    /// </summary>
    public bool IsRegistered(string kind) => _factories.ContainsKey(kind);

    /// <summary>
    /// Remove a registration. Primarily for tests that need to exercise
    /// the throw-on-unknown-kind path without tearing down the process.
    /// Returns true if the kind was registered.
    /// </summary>
    public bool Unregister(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return _factories.TryRemove(kind, out _);
    }

    /// <summary>
    /// Build the actionable error message for an unknown kind. Override to
    /// list the registry's built-ins and point at the registration entry
    /// point a plugin author would have missed.
    /// </summary>
    protected abstract string BuildUnknownKindMessage(string kind);
}
