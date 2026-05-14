#nullable enable

namespace MMP.Herald.Configuration.Json;
/// <summary>
/// JSON-facing placement rule for inserting a level relative to another level.
/// </summary>
public sealed record JsonLogLevelPlacement(
    string LevelKey,
    string Position,
    string RelativeToLevelKey);