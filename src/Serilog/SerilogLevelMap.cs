#nullable enable

using System.Collections.Generic;
using MMP.Herald.Levels;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog;

/// <summary>
/// Single canonical bidirectional map between Serilog's <see cref="LogEventLevel"/>
/// and Herald's <see cref="LogLevel"/>.
///
/// <para>
/// The map is closed over the six overlapping levels (Verbose/Debug/Information/
/// Warning/Error/Fatal). Herald's four extras (notice/success/security/metric)
/// have no Serilog counterpart — <see cref="TryToSerilog"/> returns <c>false</c>
/// for them.
/// </para>
/// </summary>
public static class SerilogLevelMap
{
    private static readonly IReadOnlyDictionary<LogEventLevel, LogLevel> _toHerald =
        new Dictionary<LogEventLevel, LogLevel>
        {
            [LogEventLevel.Verbose]     = KnownLogLevels.Verbose,
            [LogEventLevel.Debug]       = KnownLogLevels.Debug,
            [LogEventLevel.Information] = KnownLogLevels.Information,
            [LogEventLevel.Warning]     = KnownLogLevels.Warning,
            [LogEventLevel.Error]       = KnownLogLevels.Error,
            [LogEventLevel.Fatal]       = KnownLogLevels.Fatal,
        };

    private static readonly IReadOnlyDictionary<string, LogEventLevel> _toSerilog = BuildReverse();

    /// <summary>
    /// Map a Serilog <see cref="LogEventLevel"/> to its Herald <see cref="LogLevel"/>
    /// counterpart. Always succeeds for every defined <see cref="LogEventLevel"/> value.
    /// </summary>
    public static LogLevel ToHerald(LogEventLevel level) => _toHerald[level];

    /// <summary>
    /// Try to map a Herald <see cref="LogLevel"/> to its Serilog
    /// <see cref="LogEventLevel"/> counterpart.
    /// Returns <c>false</c> for Herald-only extras (notice/success/security/metric).
    /// </summary>
    public static bool TryToSerilog(LogLevel heraldLevel, out LogEventLevel result)
        => _toSerilog.TryGetValue(heraldLevel.Key, out result);

    // Build the reverse map keyed by Herald level Key.
    // The forward map is the source of truth — if a Herald level's key
    // matches one of the six canonical entries, it maps back to Serilog.
    private static IReadOnlyDictionary<string, LogEventLevel> BuildReverse()
    {
        var d = new Dictionary<string, LogEventLevel>(_toHerald.Count, System.StringComparer.Ordinal);
        foreach (var kv in _toHerald)
            d[kv.Value.Key] = kv.Key;
        return d;
    }
}
