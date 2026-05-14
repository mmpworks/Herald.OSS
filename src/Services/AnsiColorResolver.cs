#nullable enable

using System;
using System.Collections.Generic;

namespace MMP.Herald.Services;

/// <summary>
/// Resolves color names, hex codes, and RGB values to ANSI escape sequences.
/// Ships with ~30 built-in named colors. Supports three inline formats:
///   - Named: "red", "gold", "sky_blue"
///   - 256-color: "ansi256:208"
///   - RGB hex: "#FF6B35" or "rgb:255,107,53"
///
/// Reusable beyond logging - suitable for any CLI tool needing colored output.
/// </summary>
public sealed class AnsiColorResolver
{
    private static readonly Dictionary<string, string> BuiltInForegroundCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = "\x1b[30m", ["red"] = "\x1b[91m", ["green"] = "\x1b[92m",
        ["yellow"] = "\x1b[93m", ["blue"] = "\x1b[94m", ["magenta"] = "\x1b[95m",
        ["cyan"] = "\x1b[96m", ["white"] = "\x1b[97m",
        ["gray"] = "\x1b[37m", ["grey"] = "\x1b[37m",
        ["dim_gray"] = "\x1b[90m", ["dim_grey"] = "\x1b[90m",
        ["dark_gray"] = "\x1b[90m", ["dark_grey"] = "\x1b[90m",
        ["light_gray"] = "\x1b[37m", ["light_grey"] = "\x1b[37m",
        ["orange"] = "\x1b[38;5;208m", ["pink"] = "\x1b[38;5;213m",
        ["purple"] = "\x1b[38;5;141m", ["brown"] = "\x1b[38;5;130m",
        ["lime"] = "\x1b[38;5;118m", ["teal"] = "\x1b[38;5;30m",
        ["gold"] = "\x1b[38;5;220m", ["coral"] = "\x1b[38;5;209m",
        ["salmon"] = "\x1b[38;5;173m", ["sky_blue"] = "\x1b[38;5;117m",
        ["olive"] = "\x1b[38;5;142m", ["crimson"] = "\x1b[38;5;160m",
        ["slate"] = "\x1b[38;5;60m", ["indigo"] = "\x1b[38;5;54m",
    };

    private static readonly Dictionary<string, string> BuiltInBackgroundCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = "\x1b[40m", ["red"] = "\x1b[101m", ["green"] = "\x1b[102m",
        ["yellow"] = "\x1b[103m", ["blue"] = "\x1b[104m", ["magenta"] = "\x1b[105m",
        ["cyan"] = "\x1b[106m", ["white"] = "\x1b[107m",
        ["gray"] = "\x1b[47m", ["grey"] = "\x1b[47m",
        ["dim_gray"] = "\x1b[100m", ["dim_grey"] = "\x1b[100m",
        ["dark_gray"] = "\x1b[100m", ["dark_grey"] = "\x1b[100m",
        ["light_gray"] = "\x1b[47m", ["light_grey"] = "\x1b[47m",
        ["orange"] = "\x1b[48;5;208m", ["pink"] = "\x1b[48;5;213m",
        ["purple"] = "\x1b[48;5;141m", ["brown"] = "\x1b[48;5;130m",
        ["lime"] = "\x1b[48;5;118m", ["teal"] = "\x1b[48;5;30m",
        ["gold"] = "\x1b[48;5;220m", ["coral"] = "\x1b[48;5;209m",
        ["salmon"] = "\x1b[48;5;173m", ["sky_blue"] = "\x1b[48;5;117m",
        ["olive"] = "\x1b[48;5;142m", ["crimson"] = "\x1b[48;5;160m",
        ["slate"] = "\x1b[48;5;60m", ["indigo"] = "\x1b[48;5;54m",
    };

    private readonly Dictionary<string, string> _foregroundCodes;
    private readonly Dictionary<string, string> _backgroundCodes;

    public AnsiColorResolver() {
        _foregroundCodes = new Dictionary<string, string>(BuiltInForegroundCodes, StringComparer.OrdinalIgnoreCase);
        _backgroundCodes = new Dictionary<string, string>(BuiltInBackgroundCodes, StringComparer.OrdinalIgnoreCase);
    }

    public AnsiColorResolver(IReadOnlyDictionary<string, int> customColors) : this() {
        ArgumentNullException.ThrowIfNull(customColors);
        foreach (var (name, index) in customColors)
        {
            RegisterColor(name, index);
        }
    }

    /// <summary>Register a custom named color using an ANSI 256-color index (0-255).</summary>
    public void RegisterColor(string name, int ansi256Index) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (ansi256Index < 0 || ansi256Index > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(ansi256Index), ansi256Index,
                "ANSI 256-color index must be between 0 and 255.");
        }

        _foregroundCodes[name] = $"\x1b[38;5;{ansi256Index}m";
        _backgroundCodes[name] = $"\x1b[48;5;{ansi256Index}m";
    }

    /// <summary>Register a custom named color using RGB values (0-255 each).</summary>
    public void RegisterColor(string name, int red, int green, int blue) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ValidateRgbComponent(red, nameof(red));
        ValidateRgbComponent(green, nameof(green));
        ValidateRgbComponent(blue, nameof(blue));

        _foregroundCodes[name] = $"\x1b[38;2;{red};{green};{blue}m";
        _backgroundCodes[name] = $"\x1b[48;2;{red};{green};{blue}m";
    }

    /// <summary>Resolve a color name/spec to an ANSI foreground escape code, or null.</summary>
    public string? ResolveForeground(string colorName) {
        if (_foregroundCodes.TryGetValue(colorName, out var code))
        {
            return code;
        }

        return TryParseInlineColor(colorName, isForeground: true);
    }

    /// <summary>Resolve a color name/spec to an ANSI background escape code, or null.</summary>
    public string? ResolveBackground(string colorName) {
        if (_backgroundCodes.TryGetValue(colorName, out var code))
        {
            return code;
        }

        return TryParseInlineColor(colorName, isForeground: false);
    }

    /// <summary>
    /// Parse inline color specs: "ansi256:N", "#RRGGBB", "rgb:R,G,B".
    /// Returns null if the format is not recognized.
    /// </summary>
    public static string? TryParseInlineColor(string colorSpec, bool isForeground) {
        var prefix = isForeground ? "38" : "48";

        if (colorSpec.StartsWith("ansi256:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(colorSpec.AsSpan(8), out var index) &&
            index >= 0 && index <= 255)
        {
            return $"\x1b[{prefix};5;{index}m";
        }

        if (colorSpec.StartsWith('#') && colorSpec.Length == 7 &&
            TryParseHexPair(colorSpec, 1, out var r) &&
            TryParseHexPair(colorSpec, 3, out var g) &&
            TryParseHexPair(colorSpec, 5, out var b))
        {
            return $"\x1b[{prefix};2;{r};{g};{b}m";
        }

        if (colorSpec.StartsWith("rgb:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = colorSpec.AsSpan(4);
            var commaOne = parts.IndexOf(',');
            if (commaOne > 0)
            {
                var rest = parts[(commaOne + 1)..];
                var commaTwo = rest.IndexOf(',');
                if (commaTwo > 0 &&
                    int.TryParse(parts[..commaOne], out var rr) &&
                    int.TryParse(rest[..commaTwo], out var gg) &&
                    int.TryParse(rest[(commaTwo + 1)..], out var bb) &&
                    rr is >= 0 and <= 255 && gg is >= 0 and <= 255 && bb is >= 0 and <= 255)
                {
                    return $"\x1b[{prefix};2;{rr};{gg};{bb}m";
                }
            }
        }

        return null;
    }

    private static bool TryParseHexPair(string hex, int offset, out int value) {
        return int.TryParse(
            hex.AsSpan(offset, 2),
            System.Globalization.NumberStyles.HexNumber,
            null,
            out value);
    }

    private static void ValidateRgbComponent(int value, string name) {
        if (value < 0 || value > 255)
        {
            throw new ArgumentOutOfRangeException(name, value,
                "RGB component must be between 0 and 255.");
        }
    }
}
