#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace MMP.Herald.Levels;

/// <summary>
/// Thread-safe map of per-category log level overrides.
/// When a category has an override, its switch takes precedence over the global switch.
/// </summary>
public sealed class CategoryLevelSwitchMap
{
    private readonly ConcurrentDictionary<string, LogLevelSwitch> _overrides =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the category-specific switch if one exists, otherwise the provided global fallback.
    /// </summary>
    public LogLevelSwitch GetOrDefault(string categoryKey, LogLevelSwitch globalSwitch)
    {
        ArgumentNullException.ThrowIfNull(categoryKey);
        ArgumentNullException.ThrowIfNull(globalSwitch);

        return _overrides.TryGetValue(categoryKey, out var categorySwitch)
            ? categorySwitch
            : globalSwitch;
    }

    /// <summary>
    /// Sets or updates the minimum level for a specific category.
    /// </summary>
    public void SetCategoryLevel(string categoryKey, LogLevel level)
    {
        ArgumentNullException.ThrowIfNull(categoryKey);
        ArgumentNullException.ThrowIfNull(level);

        _overrides.AddOrUpdate(
            categoryKey,
            _ => LogLevelSwitch.For(level),
            (_, existing) =>
            {
                existing.MinimumLevel = level;
                return existing;
            });
    }

    /// <summary>
    /// Removes a category-specific override, reverting to the global minimum level.
    /// </summary>
    public bool RemoveCategoryOverride(string categoryKey)
    {
        ArgumentNullException.ThrowIfNull(categoryKey);
        return _overrides.TryRemove(categoryKey, out _);
    }

    /// <summary>
    /// Returns all current category overrides for diagnostics.
    /// </summary>
    public IReadOnlyDictionary<string, LogLevelSwitch> GetAllOverrides()
    {
        return _overrides;
    }
}
