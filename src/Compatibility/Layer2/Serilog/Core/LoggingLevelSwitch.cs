#nullable enable

// Layer-2 mirror of Serilog.Core.LoggingLevelSwitch.
// Wraps the Layer-1 MMP.Herald.Serilog.Core.LoggingLevelSwitch which in turn
// wraps the native Herald LogLevelSwitch. Two-layer wrap, zero logic in each layer.
//
// Layer-1 twin: MMP.Herald.Serilog.Core.LoggingLevelSwitch

using L1Core = MMP.Herald.Serilog.Core;

namespace Serilog.Core;

/// <summary>
/// Serilog-compatible dynamic level switch exported under the <c>Serilog</c> assembly identity.
/// Allows runtime control of the pipeline's minimum level without rebuilding the logger.
/// </summary>
public sealed class LoggingLevelSwitch
{
    // The Layer-1 switch is exposed internally so LoggerConfiguration.MinimumLevel.ControlledBy()
    // in Configuration/LoggerMinimumLevelConfiguration.cs can forward it into the Layer-1 builder.
    internal readonly L1Core.LoggingLevelSwitch Inner;

    /// <summary>
    /// Initialise with an explicit <paramref name="minimumLevel"/>.
    /// Defaults to <see cref="Events.LogEventLevel.Information"/> to match Serilog's default.
    /// </summary>
    public LoggingLevelSwitch(Events.LogEventLevel minimumLevel = Events.LogEventLevel.Information)
        => Inner = new L1Core.LoggingLevelSwitch((MMP.Herald.Serilog.Events.LogEventLevel)(int)minimumLevel);

    /// <summary>Gets or sets the current minimum level. Thread-safe.</summary>
    public Events.LogEventLevel MinimumLevel
    {
        get => (Events.LogEventLevel)(int)Inner.MinimumLevel;
        set => Inner.MinimumLevel = (MMP.Herald.Serilog.Events.LogEventLevel)(int)value;
    }
}
