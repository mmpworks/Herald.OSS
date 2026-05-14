#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Levels;
/// <summary>
/// Owns log level ordering and rank lookups.
/// </summary>
public interface ILogLevelRegistry
{
    void Register(LogLevel level);
    void RegisterBefore(string existingLevelKey, LogLevel level);
    void RegisterAfter(string existingLevelKey, LogLevel level);

    bool Contains(string levelKey);
    bool Contains(LogLevel level);

    LogLevel? GetByKeyOrNull(string levelKey);

    int GetRank(LogLevel level);
    RegisteredLogLevel GetRegisteredLevel(LogLevel level);
    IReadOnlyList<RegisteredLogLevel> GetRegisteredLevels();

    bool IsAtOrAbove(LogLevel candidate, LogLevel minimum);
    bool IsBelow(LogLevel candidate, LogLevel minimum);

    string DumpLevels();
}