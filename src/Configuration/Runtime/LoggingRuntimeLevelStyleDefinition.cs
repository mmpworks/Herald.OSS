// Lines 1-17
#nullable enable

namespace MMP.Herald.Configuration.Runtime;
/// <summary>
/// Runtime level style definition normalized from transport config.
/// </summary>
public sealed record LoggingRuntimeLevelStyleDefinition(
    string LevelKey,
    string ColorName,
    bool UseBold = false,
    bool UseItalic = false,
    string? BackgroundColorName = null);