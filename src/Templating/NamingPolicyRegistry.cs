// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using MMP.Herald.Templating.NamingPolicies;

namespace MMP.Herald.Templating;

/// <summary>
/// Process-wide registry that maps a string identifier
/// (<c>"pascal"</c> / <c>"camel"</c> / <c>"snake"</c> / custom) to an
/// <see cref="IPropertyNamingPolicy"/> instance. The registry exists so
/// <c>QuickLogBuilder.Reload(json)</c> can rehydrate a configured policy
/// from a string id stored in the JSON.
///
/// <para>
/// The three built-ins (<see cref="PascalCasePolicy"/>, <see cref="CamelCasePolicy"/>,
/// <see cref="SnakeCasePolicy"/>) self-register eagerly in this type's static
/// constructor. Consumers register custom policies before the first
/// <c>Reload(json)</c> call:
/// </para>
///
/// <code>
/// public static class Bootstrap
/// {
///     [ModuleInitializer]
///     public static void RegisterPolicies()
///     {
///         NamingPolicyRegistry.Register(new KebabCasePolicy());
///     }
/// }
/// </code>
///
/// <para>
/// Threading: reads are lock-free via <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Writes (<see cref="Register"/>) use a short critical section to enforce
/// uniqueness without TOCTOU between the "is this id taken" check and the add.
/// </para>
/// </summary>
public static class NamingPolicyRegistry
{
    // The registry is intentionally a ConcurrentDictionary for lock-free reads.
    // The Register write path takes a separate lock so the type-comparison can
    // be made atomic with the insert.
    private static readonly ConcurrentDictionary<string, IPropertyNamingPolicy> _byId =
        new(StringComparer.Ordinal);

    private static readonly object _writeLock = new();

    // Eager registration in the type ctor — runs once on first touch, before
    // any caller can race against it. Built-ins are always available.
    static NamingPolicyRegistry()
    {
        _byId[PascalCasePolicy.Instance.Id] = PascalCasePolicy.Instance;
        _byId[CamelCasePolicy.Instance.Id] = CamelCasePolicy.Instance;
        _byId[SnakeCasePolicy.Instance.Id] = SnakeCasePolicy.Instance;
    }

    /// <summary>
    /// Register a custom policy. Idempotent for the same policy type
    /// (re-registration of the exact same Type is a no-op). Throws
    /// <see cref="DuplicatePolicyIdException"/> if a different policy type
    /// already owns the same id.
    /// </summary>
    /// <param name="policy">The policy to register. Must not be null.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="policy"/> is null.</exception>
    /// <exception cref="DuplicatePolicyIdException">
    /// When a different policy type is already registered under
    /// <c>policy.Id</c>. Idempotent for the same type to support
    /// <c>[ModuleInitializer]</c> races and repeated bootstrap calls.
    /// </exception>
    public static void Register(IPropertyNamingPolicy policy)
    {
        if (policy is null) throw new ArgumentNullException(nameof(policy));

        var id = policy.Id;
        var newType = policy.GetType();

        // Single critical section so the existence check and the type-match
        // check happen atomically with the insert. Two threads racing on the
        // same (id, type) collapse to one register; two threads racing on
        // (id, different types) both see the conflict cleanly.
        lock (_writeLock)
        {
            if (_byId.TryGetValue(id, out var existing))
            {
                if (existing.GetType() == newType)
                {
                    return; // idempotent — same policy type re-registering
                }

                throw new DuplicatePolicyIdException(id, existing.GetType(), newType);
            }

            _byId[id] = policy;
        }
    }

    /// <summary>
    /// Resolve a policy by its <see cref="IPropertyNamingPolicy.Id"/>. Returns
    /// the registered instance if known; throws otherwise.
    /// </summary>
    /// <param name="id">The policy identifier (e.g. <c>"pascal"</c>).</param>
    /// <exception cref="UnknownNamingPolicyException">
    /// When no policy with the given id has been registered.
    /// </exception>
    public static IPropertyNamingPolicy Resolve(string id)
    {
        if (id is null) throw new ArgumentNullException(nameof(id));
        if (_byId.TryGetValue(id, out var policy)) return policy;
        throw new UnknownNamingPolicyException(id);
    }

    /// <summary>
    /// Non-throwing variant of <see cref="Resolve"/>. Used by hot-reload paths
    /// that prefer to degrade to the previously-active policy rather than
    /// fail the reload outright.
    /// </summary>
    public static bool TryResolve(string id, out IPropertyNamingPolicy? policy)
    {
        return _byId.TryGetValue(id, out policy);
    }

    /// <summary>
    /// Snapshot of currently-registered policy ids. Primarily useful for
    /// diagnostics and tests. Order is unspecified.
    /// </summary>
    public static IReadOnlyCollection<string> RegisteredIds => _byId.Keys.ToArray();
}
