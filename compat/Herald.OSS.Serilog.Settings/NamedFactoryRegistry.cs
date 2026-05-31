// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

#nullable enable

using System;
using System.Collections.Generic;

namespace Herald.OSS.Serilog.Settings;

/// <summary>
/// Internal, generic name-to-factory map shared by both public registries.
///
/// <para>
/// Case-insensitive by name (<see cref="StringComparer.OrdinalIgnoreCase"/>).
/// Collision policy: <see cref="Register"/> throws on duplicate names rather
/// than silently overwriting — prevents accidental shadowing of built-ins.
/// </para>
/// </summary>
/// <typeparam name="TFactory">The delegate type stored per name.</typeparam>
internal sealed class NamedFactoryRegistry<TFactory>
    where TFactory : Delegate
{
    private readonly Dictionary<string, TFactory> _map =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether mutations via <see cref="Register"/> are blocked.</summary>
    private bool _sealed;

    /// <summary>
    /// Register a name/factory pair.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    ///   <paramref name="name"/> or <paramref name="factory"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///   A factory is already registered under <paramref name="name"/>
    ///   (collision-throw policy), or the registry has been sealed.
    /// </exception>
    internal void Register(string name, TFactory factory)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(factory);

        if (_sealed)
            throw new InvalidOperationException(
                $"This registry is sealed and does not accept new registrations. " +
                $"Use {nameof(LoggerSinkRegistry)}.{nameof(LoggerSinkRegistry.CreateDefault)}() " +
                $"to obtain a mutable copy.");

        if (_map.ContainsKey(name))
            throw new InvalidOperationException(
                $"A factory is already registered under the name '{name}'. " +
                $"Registration collision is treated as an error — " +
                $"use a distinct name or obtain a fresh registry via CreateDefault().");

        _map[name] = factory;
    }

    /// <returns>
    ///   <c>true</c> when <paramref name="name"/> maps to a registered factory;
    ///   <c>false</c> when it does not or is null/blank.
    /// </returns>
    internal bool Contains(string? name)
        => !string.IsNullOrWhiteSpace(name) && _map.ContainsKey(name);

    /// <summary>
    /// Attempt to retrieve the factory registered under <paramref name="name"/>.
    /// </summary>
    internal bool TryGet(string name, out TFactory? factory)
        => _map.TryGetValue(name, out factory);

    /// <summary>
    /// Seal the registry so no further registrations are accepted.
    /// Called by <see cref="LoggerSinkRegistry.Default"/> and
    /// <see cref="LoggerEnricherRegistry.Default"/> after pre-seeding.
    /// </summary>
    internal void Seal() => _sealed = true;
}
