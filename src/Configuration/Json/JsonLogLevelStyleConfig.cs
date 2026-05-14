// Lines 1-17
#nullable enable

namespace MMP.Herald.Configuration.Json;
/// <summary>
/// JSON-facing style definition for a level.
/// These are presentation hints that adapters may choose to honor.
/// </summary>
public sealed record JsonLogLevelStyleConfig(
    string LevelKey,
    string ColorName,
    bool UseBold = false,
    bool UseItalic = false,
    string? BackgroundColorName = null);