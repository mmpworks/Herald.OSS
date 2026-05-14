#nullable enable

namespace MMP.Herald.Output.Rendering;
/// <summary>
/// Presentation metadata for a log level.
/// </summary>
public sealed record LogLevelStyle(
    string ColorName,
    bool UseBold = false,
    bool UseItalic = false);