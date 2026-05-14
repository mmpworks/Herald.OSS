#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Events;
using MMP.Herald.Levels;

namespace MMP.Herald.Filters;

/// <summary>
/// Level filter that reads from a mutable LogLevelSwitch on each call,
/// with optional per-category and per-context-value overrides.
/// Replaces the fixed LevelFilter when dynamic level switching is enabled.
///
/// Override priority (first match wins):
///   1. Context-value overrides (e.g., entityId="npc_47" -> debug)
///   2. Category overrides (e.g., category="AI" -> trace)
///   3. Global switch
/// </summary>
public sealed class SwitchableLevelFilter : ILogFilter
{
    private readonly ILogLevelRegistry _levelRegistry;
    private readonly LogLevelSwitch _globalSwitch;
    private readonly CategoryLevelSwitchMap? _categoryMap;
    private readonly IReadOnlyList<ContextLevelSwitchMap>? _contextMaps;

    public SwitchableLevelFilter(
        ILogLevelRegistry levelRegistry,
        LogLevelSwitch globalSwitch,
        CategoryLevelSwitchMap? categoryMap = null,
        IReadOnlyList<ContextLevelSwitchMap>? contextMaps = null)
    {
        _levelRegistry = levelRegistry ?? throw new ArgumentNullException(nameof(levelRegistry));
        _globalSwitch = globalSwitch ?? throw new ArgumentNullException(nameof(globalSwitch));
        _categoryMap = categoryMap;
        _contextMaps = contextMaps;
    }

    public bool Allow(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        // Audit events bypass minimum level filtering entirely.
        if (IsAuditEvent(logEvent)) return true;

        // Context-value overrides take highest priority
        if (_contextMaps is { Count: > 0 })
        {
            foreach (var contextMap in _contextMaps)
            {
                var contextSwitch = contextMap.TryGetSwitch(logEvent.Context);
                if (contextSwitch is not null)
                {
                    return _levelRegistry.IsAtOrAbove(logEvent.Level, contextSwitch.MinimumLevel);
                }
            }
        }

        var effectiveSwitch = _categoryMap is not null
            ? _categoryMap.GetOrDefault(logEvent.Category.Value, _globalSwitch)
            : _globalSwitch;

        return _levelRegistry.IsAtOrAbove(logEvent.Level, effectiveSwitch.MinimumLevel);
    }

    private static bool IsAuditEvent(LogEvent logEvent)
    {
        return logEvent.Context.TryGetValue(Services.LogContextKeys.Audit, out var value)
               && value is string text
               && string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
    }

    public string Describe()
    {
        return $"SwitchableLevelFilter - global: {_globalSwitch.MinimumLevel.DisplayName}" +
               (_categoryMap is not null ? " (with category overrides)" : "");
    }

    // -- Inspection --

    /// <summary>The mutable global level switch. Change MinimumLevel at runtime.</summary>
    public LogLevelSwitch GlobalSwitch => _globalSwitch;

    /// <summary>Per-category level overrides, or null if not configured.</summary>
    public CategoryLevelSwitchMap? CategoryMap => _categoryMap;

    /// <summary>The level registry used for rank comparison.</summary>
    public ILogLevelRegistry LevelRegistry => _levelRegistry;
}
