#nullable enable

using static MMP.Herald.Output.Rendering.Themes.ConfigurableConsoleTheme;

namespace MMP.Herald.Output.Rendering.Themes;

/// <summary>
/// Built-in console themes inspired by Serilog's theme system.
/// Each theme provides defaults for all output elements and level-specific overrides.
/// </summary>
public static class BuiltInConsoleThemes
{
    // Shared level badge styles - eliminates duplication across themes.
    private static readonly OutputStyle SuccessBadge = new("white", UseBold: true, BackgroundColorName: "green");
    private static readonly OutputStyle WarnBadgeDark = new("black", UseBold: true, BackgroundColorName: "yellow");
    private static readonly OutputStyle WarnBadgeLight = new("black", UseBold: true, BackgroundColorName: "gold");
    private static readonly OutputStyle ErrorBadge = new("white", UseBold: true, BackgroundColorName: "red");
    private static readonly OutputStyle CriticalBadge = new("white", UseBold: true, UseItalic: true, BackgroundColorName: "crimson");
    private static readonly OutputStyle SecurityBadgeDark = new("white", UseBold: true, BackgroundColorName: "magenta");
    private static readonly OutputStyle SecurityBadgeLight = new("white", UseBold: true, BackgroundColorName: "purple");
    /// <summary>
    /// No styling — all elements unstyled.
    /// </summary>
    public static IConsoleTheme None { get; } = new ConfigurableConsoleTheme([]);

    /// <summary>
    /// Dark background theme with muted colors.
    /// </summary>
    public static IConsoleTheme Dark { get; } = new ConfigurableConsoleTheme(
    [
        new ThemeEntry(OutputElementKind.Timestamp.Instance, new OutputStyle("dim_gray")),
        new ThemeEntry(OutputElementKind.LevelText.Instance, new OutputStyle("white")),
        new ThemeEntry(OutputElementKind.Category.Instance, new OutputStyle(UseItalic: true)),
        new ThemeEntry(OutputElementKind.Separator.Instance, new OutputStyle("dim_gray")),
        new ThemeEntry(OutputElementKind.MessageText.Instance, new OutputStyle()),
        new ThemeEntry(OutputElementKind.PropertyName.Instance, new OutputStyle("gray")),
        new ThemeEntry(OutputElementKind.PropertyValue.Instance, new OutputStyle("gray")),
        new ThemeEntry(OutputElementKind.Punctuation.Instance, new OutputStyle("dim_gray")),
        new ThemeEntry(OutputElementKind.NullValue.Instance, new OutputStyle("dim_gray", UseItalic: true)),
        new ThemeEntry(OutputElementKind.ExceptionText.Instance, new OutputStyle("orange_red")),

        new ThemeEntry(OutputElementKind.LevelText.Instance, new OutputStyle("gray"), "verbose"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, new OutputStyle("cyan"), "debug"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, new OutputStyle("white"), "information"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, new OutputStyle("aquamarine", UseItalic: true), "metric"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, new OutputStyle("light_blue", UseBold: true), "notice"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, SuccessBadge, "success"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, WarnBadgeDark, "warning"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, ErrorBadge, "error"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, CriticalBadge, "fatal"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, SecurityBadgeDark, "security"),
    ]);

    /// <summary>
    /// Light background theme with stronger contrast.
    /// </summary>
    public static IConsoleTheme Light { get; } = new ConfigurableConsoleTheme(
    [
        new ThemeEntry(OutputElementKind.Timestamp.Instance, new OutputStyle("gray")),
        new ThemeEntry(OutputElementKind.LevelText.Instance, new OutputStyle("dark_gray")),
        new ThemeEntry(OutputElementKind.Category.Instance, new OutputStyle(UseItalic: true)),
        new ThemeEntry(OutputElementKind.Separator.Instance, new OutputStyle("gray")),
        new ThemeEntry(OutputElementKind.MessageText.Instance, new OutputStyle()),
        new ThemeEntry(OutputElementKind.PropertyName.Instance, new OutputStyle("dark_slate_gray")),
        new ThemeEntry(OutputElementKind.PropertyValue.Instance, new OutputStyle("dark_slate_gray")),
        new ThemeEntry(OutputElementKind.Punctuation.Instance, new OutputStyle("gray")),
        new ThemeEntry(OutputElementKind.NullValue.Instance, new OutputStyle("gray", UseItalic: true)),
        new ThemeEntry(OutputElementKind.ExceptionText.Instance, new OutputStyle("dark_red")),

        new ThemeEntry(OutputElementKind.LevelText.Instance, new OutputStyle("silver"), "verbose"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, new OutputStyle("dark_cyan"), "debug"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, new OutputStyle("black"), "information"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, SuccessBadge, "success"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, WarnBadgeLight, "warning"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, ErrorBadge, "error"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, CriticalBadge, "fatal"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, SecurityBadgeLight, "security"),
    ]);

    /// <summary>
    /// Serilog Literate-inspired theme with colorful, readable output.
    /// </summary>
    public static IConsoleTheme Literate { get; } = new ConfigurableConsoleTheme(
    [
        new ThemeEntry(OutputElementKind.Timestamp.Instance, new OutputStyle("dim_gray")),
        new ThemeEntry(OutputElementKind.LevelText.Instance, new OutputStyle("white", UseBold: true)),
        new ThemeEntry(OutputElementKind.Category.Instance, new OutputStyle("cornflower_blue", UseItalic: true)),
        new ThemeEntry(OutputElementKind.Separator.Instance, new OutputStyle("dim_gray")),
        new ThemeEntry(OutputElementKind.MessageText.Instance, new OutputStyle("white")),
        new ThemeEntry(OutputElementKind.PropertyName.Instance, new OutputStyle("steel_blue")),
        new ThemeEntry(OutputElementKind.PropertyValue.Instance, new OutputStyle("light_steel_blue")),
        new ThemeEntry(OutputElementKind.StringValue.Instance, new OutputStyle("light_coral")),
        new ThemeEntry(OutputElementKind.NumericValue.Instance, new OutputStyle("light_green")),
        new ThemeEntry(OutputElementKind.NullValue.Instance, new OutputStyle("cadet_blue", UseItalic: true)),
        new ThemeEntry(OutputElementKind.Punctuation.Instance, new OutputStyle("dim_gray")),
        new ThemeEntry(OutputElementKind.ExceptionText.Instance, new OutputStyle("indian_red")),

        new ThemeEntry(OutputElementKind.LevelText.Instance, new OutputStyle("dim_gray"), "verbose"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, new OutputStyle("steel_blue"), "debug"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, new OutputStyle("white"), "information"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, SuccessBadge, "success"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, WarnBadgeLight, "warning"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, new OutputStyle("white", UseBold: true, BackgroundColorName: "coral"), "error"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, CriticalBadge, "fatal"),
        new ThemeEntry(OutputElementKind.LevelText.Instance, SecurityBadgeLight, "security"),
    ]);
}
