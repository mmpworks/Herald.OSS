#nullable enable

using MMP.Herald.Levels;

namespace MMP.Herald.Configuration;

/// <summary>
/// Groups dynamic level-switching concerns: the global mutable level switch
/// and optional per-category overrides.
/// </summary>
public sealed record DynamicLevelPolicy(
    LogLevelSwitch GlobalLevelSwitch,
    CategoryLevelSwitchMap? CategoryLevelSwitches = null);
