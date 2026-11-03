using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Quick;

namespace LoggingPractices.Practices;

/// <summary>
/// Practice 2. Use log levels, and change verbosity while the process runs.
/// WithFastDynamicLevel installs FastPathDynamicLevel, which reads a
/// <see cref="LogLevelSwitch"/> on every call. Flip the switch and the next
/// call sees the new level. No restart, no rebuild.
/// </summary>
public static class LevelsAndVerbosity
{
    public static async Task RunAsync()
    {
        var levelSwitch = new LogLevelSwitch(KnownLogLevels.Information);

        await using var result = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithMinimumLevel("debug")
            .WithFastDynamicLevel(levelSwitch)
            .BuildAndCommit();

        result.Logger.Debug(LogCategory.App, "Cache warmed: first call");
        result.Logger.Information(LogCategory.App, "Switch is at {Level}, so the debug line above was dropped", levelSwitch.MinimumLevel.Key);

        levelSwitch.MinimumLevel = KnownLogLevels.Debug;
        result.Logger.Debug(LogCategory.App, "Cache warmed: second call, printed because the switch is now at debug");
    }
}
