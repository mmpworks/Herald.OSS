#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace MMP.Herald.Levels;

/// <summary>
/// Thread-safe map of per-context-value log level overrides.
/// Enables fine-grained level control based on any context key+value pair.
///
/// Inspired by Erlang/BEAM's per-process log levels:
/// "debug NPC #47 while everything else stays at Info."
///
/// Usage:
///   var entityOverrides = new ContextLevelSwitchMap("entityId");
///   entityOverrides.SetLevel("npc_47", debugLevel);
///
///   // In SwitchableLevelFilter, events with context["entityId"] = "npc_47"
///   // now pass at debug level regardless of the global minimum.
///
///   entityOverrides.RemoveOverride("npc_47"); // revert to global
/// </summary>
public sealed class ContextLevelSwitchMap
{
    private readonly string _contextKey;
    private readonly ConcurrentDictionary<string, LogLevelSwitch> _overrides =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Create a map keyed on a specific context key (e.g., "entityId", "userId", "sessionId").
    /// </summary>
    public ContextLevelSwitchMap(string contextKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contextKey);
        _contextKey = contextKey;
    }

    /// <summary>
    /// The context key this map monitors.
    /// </summary>
    public string ContextKey => _contextKey;

    /// <summary>
    /// Checks if the event's context matches a registered override.
    /// Returns the override switch if found, otherwise null.
    /// </summary>
    public LogLevelSwitch? TryGetSwitch(IReadOnlyDictionary<string, object?> context)
    {
        if (!context.TryGetValue(_contextKey, out var rawValue))
            return null;

        var key = rawValue?.ToString();
        if (key is null) return null;

        return _overrides.TryGetValue(key, out var levelSwitch) ? levelSwitch : null;
    }

    /// <summary>
    /// Set a minimum level override for a specific context value.
    /// </summary>
    public void SetLevel(string contextValue, LogLevel level)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contextValue);
        ArgumentNullException.ThrowIfNull(level);

        _overrides.AddOrUpdate(
            contextValue,
            _ => LogLevelSwitch.For(level),
            (_, existing) =>
            {
                existing.MinimumLevel = level;
                return existing;
            });
    }

    /// <summary>
    /// Remove an override, reverting to the global minimum level.
    /// </summary>
    public bool RemoveOverride(string contextValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contextValue);
        return _overrides.TryRemove(contextValue, out _);
    }

    /// <summary>
    /// Remove all overrides.
    /// </summary>
    public void Clear() => _overrides.Clear();

    /// <summary>
    /// Returns all current overrides for diagnostics.
    /// </summary>
    public IReadOnlyDictionary<string, LogLevelSwitch> GetAllOverrides() => _overrides;
}
