#nullable enable

// RollingInterval mirrors real Serilog's root namespace position
// (Serilog.RollingInterval), so it resolves under `using MMP.Herald.Serilog;`.
namespace MMP.Herald.Serilog;

/// <summary>
/// How often a file sink rolls to a new file. Mirrors
/// <c>Serilog.RollingInterval</c> so configurations that name a rolling cadence
/// compile unchanged against MMP.Herald.Serilog.
///
/// <para>
/// <b>Native coverage.</b> Herald's file engine supports four roll periods:
/// none (Infinite), hourly (Hour), daily (Day), and a fixed-minute custom window
/// (Minute → a 1-minute window). <see cref="Year"/> and <see cref="Month"/> have
/// no native equivalent — the engine does not roll on calendar-month or
/// calendar-year boundaries. Selecting them throws rather than silently rolling
/// at a different cadence, so a migrated config never logs to an unexpected file
/// layout without notice. See <c>FileSinkVerbMapper</c> for the exact map.
/// </para>
/// </summary>
public enum RollingInterval
{
    /// <summary>The log file never rolls. Maps to the native <c>none</c> period.</summary>
    Infinite,

    /// <summary>Roll once per calendar year. No native equivalent — selecting this throws.</summary>
    Year,

    /// <summary>Roll once per calendar month. No native equivalent — selecting this throws.</summary>
    Month,

    /// <summary>Roll once per day. Maps to the native <c>daily</c> period.</summary>
    Day,

    /// <summary>Roll once per hour. Maps to the native <c>hourly</c> period.</summary>
    Hour,

    /// <summary>Roll once per minute. Maps to the native <c>custom</c> period with a 1-minute window.</summary>
    Minute
}
