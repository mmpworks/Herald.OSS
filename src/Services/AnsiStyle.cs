#nullable enable

namespace MMP.Herald.Services;

/// <summary>
/// ANSI escape codes for text styling. Use these directly for simple cases,
/// or use AnsiStyleItems for composable style combinations.
///
/// Coverage:
///   Bold, Dim, Italic, Underline, DoubleUnderline, Reverse, Strike
///   Each has an On code and an Off code, plus a Reset to clear all.
///
/// Usage (direct):
///   Console.WriteLine($"{AnsiStyle.Bold}Important{AnsiStyle.Reset}");
///   Console.WriteLine($"{AnsiStyle.Italic}{AnsiStyle.Underline}fancy{AnsiStyle.Reset}");
///
/// Usage (fluent via AnsiStyleItems):
///   var style = AnsiStyleItems.Create().Bold().Italic().Color(KnownAnsiColors.Cyan);
///   Console.WriteLine(style.Apply("Hello World"));
///
/// Usage (expand for manual control):
///   var (open, close) = style.Expand();
///   builder.Append(open).Append(text).Append(close);
/// </summary>
public static class AnsiStyle
{
    /// <summary>Reset all styles and colors to terminal default.</summary>
    public const string Reset = "\x1b[0m";

    // -- Style On codes --

    /// <summary>Bold / intense text. Widely supported.</summary>
    public const string Bold = "\x1b[1m";

    /// <summary>Dim / faint text. Lighter than normal. Good support.</summary>
    public const string Dim = "\x1b[2m";

    /// <summary>Italic text. Not supported in all terminals.</summary>
    public const string Italic = "\x1b[3m";

    /// <summary>Single underline. Excellent support.</summary>
    public const string Underline = "\x1b[4m";

    /// <summary>Reverse / invert foreground and background. Excellent support.</summary>
    public const string Reverse = "\x1b[7m";

    /// <summary>Strikethrough text. Very good support.</summary>
    public const string Strike = "\x1b[9m";

    /// <summary>Double underline. Moderate support - some terminals render as bold underline.</summary>
    public const string DoubleUnderline = "\x1b[21m";

    // -- Style Off codes --

    /// <summary>Turn off Bold and Dim (both use the same off code).</summary>
    public const string NoBold = "\x1b[22m";

    /// <summary>Turn off Italic.</summary>
    public const string NoItalic = "\x1b[23m";

    /// <summary>Turn off Underline and DoubleUnderline.</summary>
    public const string NoUnderline = "\x1b[24m";

    /// <summary>Turn off Reverse.</summary>
    public const string NoReverse = "\x1b[27m";

    /// <summary>Turn off Strikethrough.</summary>
    public const string NoStrike = "\x1b[29m";

    // -- Quick helpers for common single-style wrapping --

    /// <summary>Wrap text in Bold + Reset.</summary>
    public static string BoldText(string text) => $"{Bold}{text}{Reset}";

    /// <summary>Wrap text in Italic + Reset.</summary>
    public static string ItalicText(string text) => $"{Italic}{text}{Reset}";

    /// <summary>Wrap text in Underline + Reset.</summary>
    public static string Underlined(string text) => $"{Underline}{text}{Reset}";

    /// <summary>Wrap text in Strikethrough + Reset.</summary>
    public static string Strikethrough(string text) => $"{Strike}{text}{Reset}";

    /// <summary>Wrap text in Reverse + Bold + Reset (high visibility).</summary>
    public static string Highlight(string text) => $"{Reverse}{Bold}{text}{Reset}";

    /// <summary>Wrap text in Dim + Reset.</summary>
    public static string Dimmed(string text) => $"{Dim}{text}{Reset}";

    /// <summary>
    /// Apply multiple style codes to text with Reset at the end.
    /// Usage: AnsiStyle.Style("hello", AnsiStyle.Bold, AnsiStyle.Italic)
    /// </summary>
    public static string Style(string text, params string[] styles) {
        return string.Concat(styles) + text + Reset;
    }
}
