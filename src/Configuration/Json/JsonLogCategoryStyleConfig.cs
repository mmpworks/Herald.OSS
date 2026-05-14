#nullable enable

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing per-category style configuration for console output. The
/// category is the <c>Category</c> dimension on <c>LogEvent</c>; the
/// presentation-side term is "channel". Parallels <see cref="JsonPropertyStyleConfig"/>.
/// </summary>
public sealed record JsonLogCategoryStyleConfig(
    string CategoryName,
    string? ColorName = null,
    bool UseBold = false,
    bool UseItalic = false,
    string? BackgroundColorName = null);
