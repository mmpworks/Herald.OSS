#nullable enable

namespace MMP.Herald.Levels;
/// <summary>
/// Registry view of a level and its current rank.
/// </summary>
public sealed record RegisteredLogLevel(
    LogLevel Level,
    int Rank);