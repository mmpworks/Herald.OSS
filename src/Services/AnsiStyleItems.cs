#nullable enable

using System.Text;

namespace MMP.Herald.Services;

/// <summary>
/// Immutable value object representing a combination of ANSI text styles.
/// Acts as a DI container for style choices - pass one object instead of many parameters.
///
/// Usage:
///   var style = AnsiStyleItems.Create().Bold().Italic().Color(KnownAnsiColors.Cyan);
///   var styled = style.Apply("Hello World");
///   var (open, close) = style.Expand();  // get raw codes for manual use
/// </summary>
public sealed record AnsiStyleItems(
    bool IsBold = false,
    bool IsDim = false,
    bool IsItalic = false,
    bool IsUnderline = false,
    bool IsDoubleUnderline = false,
    bool IsReverse = false,
    bool IsStrike = false,
    string? ForegroundColor = null,
    string? BackgroundColor = null)
{

    /// <summary>Create an empty style with no formatting.</summary>
    public static AnsiStyleItems Create() => new();

    // -- Fluent style setters (return new immutable instance) --

    public AnsiStyleItems Bold() => this with { IsBold = true };
    public AnsiStyleItems Dim() => this with { IsDim = true };
    public AnsiStyleItems Italic() => this with { IsItalic = true };
    public AnsiStyleItems Underline() => this with { IsUnderline = true };
    public AnsiStyleItems DoubleUnderline() => this with { IsDoubleUnderline = true };
    public AnsiStyleItems Reverse() => this with { IsReverse = true };
    public AnsiStyleItems Strike() => this with { IsStrike = true };
    public AnsiStyleItems Color(string colorName) => this with { ForegroundColor = colorName };
    public AnsiStyleItems Background(string colorName) => this with { BackgroundColor = colorName };

    /// <summary>
    /// Apply all styles to text, wrapping with open codes and reset.
    /// </summary>
    public string Apply(string text) {
        var (open, close) = Expand();
        if (open.Length == 0) return text;
        return $"{open}{text}{close}";
    }

    /// <summary>
    /// Expand into raw ANSI codes. Returns (openCodes, closeCodes).
    /// Use when you need manual control over where codes are inserted.
    /// </summary>
    public (string Open, string Close) Expand() {
        var open = new StringBuilder();

        if (ForegroundColor is not null)
        {
            var code = AnsiColorResolver.TryParseInlineColor(ForegroundColor, isForeground: true);
            // If inline parse fails, try as a named color via a fresh resolver
            // For static usage, use the escape code directly from the known set
            open.Append(code ?? ResolveNamedForeground(ForegroundColor));
        }

        if (BackgroundColor is not null)
        {
            var code = AnsiColorResolver.TryParseInlineColor(BackgroundColor, isForeground: false);
            open.Append(code ?? ResolveNamedBackground(BackgroundColor));
        }

        if (IsBold) open.Append(AnsiStyle.Bold);
        if (IsDim) open.Append(AnsiStyle.Dim);
        if (IsItalic) open.Append(AnsiStyle.Italic);
        if (IsUnderline) open.Append(AnsiStyle.Underline);
        if (IsDoubleUnderline) open.Append(AnsiStyle.DoubleUnderline);
        if (IsReverse) open.Append(AnsiStyle.Reverse);
        if (IsStrike) open.Append(AnsiStyle.Strike);

        if (open.Length == 0) return ("", "");
        return (open.ToString(), AnsiStyle.Reset);
    }

    /// <summary>
    /// Check if any style is set.
    /// </summary>
    public bool HasStyle =>
        IsBold || IsDim || IsItalic || IsUnderline || IsDoubleUnderline ||
        IsReverse || IsStrike || ForegroundColor is not null || BackgroundColor is not null;

    // Shared resolver for named color lookup - avoids allocation per Expand() call
    private static readonly AnsiColorResolver SharedResolver = new();

    private static string ResolveNamedForeground(string name) =>
        SharedResolver.ResolveForeground(name) ?? "";

    private static string ResolveNamedBackground(string name) =>
        SharedResolver.ResolveBackground(name) ?? "";
}
