#nullable enable

using System.Threading;

namespace MMP.Herald.Levels;

/// <summary>
/// Thread-safe mutable holder for a log level.
/// Intentionally a class (not a record) because this is designed to be mutated at runtime.
/// Uses Volatile.Read/Write for lock-free thread safety on the immutable LogLevel reference.
/// </summary>
public sealed class LogLevelSwitch
{
    private LogLevel _currentLevel;

    public LogLevelSwitch(LogLevel initialLevel)
    {
        _currentLevel = initialLevel ?? throw new System.ArgumentNullException(nameof(initialLevel));
    }

    /// <summary>
    /// The current minimum level. Reads and writes are thread-safe without locking.
    /// </summary>
    public LogLevel MinimumLevel
    {
        get => Volatile.Read(ref _currentLevel);
        set => Volatile.Write(ref _currentLevel, value ?? throw new System.ArgumentNullException(nameof(value)));
    }

    public static LogLevelSwitch For(LogLevel level)
    {
        return new LogLevelSwitch(level);
    }

    public override string ToString()
    {
        return $"LogLevelSwitch({MinimumLevel.DisplayName})";
    }
}
