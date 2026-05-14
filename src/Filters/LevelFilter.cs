#nullable enable

using System;
using MMP.Herald.Events;
using MMP.Herald.Levels;

namespace MMP.Herald.Filters;
/// <summary>
/// Allows events at or above a minimum severity according to the registry ordering.
/// </summary>
public sealed class LevelFilter : ILogFilter
{
    private readonly ILogLevelRegistry _levelRegistry;
    private readonly LogLevel _minimumLevel;

    public LevelFilter(
        ILogLevelRegistry levelRegistry,
        LogLevel minimumLevel)
    {
        _levelRegistry = levelRegistry;
        _minimumLevel = minimumLevel;
    }

    public bool Allow(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        // Audit events bypass minimum level filtering entirely.
        // They must always reach the pipeline so audit sinks can capture them.
        if (IsAuditEvent(logEvent)) return true;

        return _levelRegistry.IsAtOrAbove(logEvent.Level, _minimumLevel);
    }

    private static bool IsAuditEvent(LogEvent logEvent)
    {
        return logEvent.Context.TryGetValue(Services.LogContextKeys.Audit, out var value)
               && value is string text
               && string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
    }

    public string Describe()
    {
        var minimumRank = _levelRegistry.GetRank(_minimumLevel);
        return $"LevelFilter - minimum level: {_minimumLevel.DisplayName} (rank {minimumRank})";
    }

    // -- Inspection --

    /// <summary>The minimum level this filter enforces.</summary>
    public LogLevel MinimumLevel => _minimumLevel;

    /// <summary>The level registry used for rank comparison.</summary>
    public ILogLevelRegistry LevelRegistry => _levelRegistry;
}