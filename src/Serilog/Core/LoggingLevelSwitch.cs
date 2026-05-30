#nullable enable

using MMP.Herald.Levels;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Core;

/// <summary>
/// Serilog-shaped mirror of <see cref="LogLevelSwitch"/> — structural match
/// for callers that depend on <c>Serilog.Core.LoggingLevelSwitch</c>.
///
/// <para>
/// Holds a native <see cref="LogLevelSwitch"/> internally. The
/// <see cref="MinimumLevel"/> property forwards through
/// <see cref="SerilogLevelMap"/> in both directions so callers see
/// <see cref="LogEventLevel"/> values while Herald sees <see cref="LogLevel"/>
/// objects.
/// </para>
///
/// <para>
/// Herald-only extras (notice/success/security/metric) have no Serilog
/// counterpart. If the native switch is set to one of those levels,
/// <see cref="MinimumLevel"/> returns <see cref="LogEventLevel.Verbose"/> (0)
/// — the least-restrictive fallback, which keeps events flowing rather than
/// silently dropping them.
/// </para>
/// </summary>
public sealed class LoggingLevelSwitch
{
    /// <summary>
    /// The underlying Herald switch. Exposed <c>internal</c> so the translator
    /// layer (LoggerConfiguration, P2 Task 2) can wire it directly into the
    /// pipeline without re-boxing through the Serilog surface.
    /// </summary>
    internal LogLevelSwitch Native { get; }

    /// <summary>
    /// Initialise with an explicit <paramref name="minimumLevel"/>.
    /// Defaults to <see cref="LogEventLevel.Information"/> to match
    /// Serilog's own default.
    /// </summary>
    public LoggingLevelSwitch(LogEventLevel minimumLevel = LogEventLevel.Information)
        => Native = new LogLevelSwitch(SerilogLevelMap.ToHerald(minimumLevel));

    /// <summary>
    /// Gets or sets the current minimum level.
    /// Reads and writes are thread-safe (delegated to <see cref="LogLevelSwitch"/>
    /// which uses <see cref="System.Threading.Volatile"/>).
    /// </summary>
    public LogEventLevel MinimumLevel
    {
        get
        {
            // TryToSerilog returns false for Herald-only extras.
            // Fall back to Verbose (0) — least-restrictive — so callers do not
            // silently lose events when a Herald-only level is active.
            return SerilogLevelMap.TryToSerilog(Native.MinimumLevel, out var level)
                ? level
                : LogEventLevel.Verbose;
        }
        set => Native.MinimumLevel = SerilogLevelMap.ToHerald(value);
    }
}
