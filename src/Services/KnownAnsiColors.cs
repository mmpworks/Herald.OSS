#nullable enable

namespace MMP.Herald.Services;

/// <summary>
/// Well-known color names supported by Herald's ANSI console output.
/// Use these instead of raw strings with WithPropertyStyle(), theme configuration,
/// and AnsiColorResolver.
///
/// All 34 built-in named colors, plus documentation of dynamic format support.
/// </summary>
public static class KnownAnsiColors
{
    // -- Standard 8 ANSI colors --
    public const string Black = "black";
    public const string Red = "red";
    public const string Green = "green";
    public const string Yellow = "yellow";
    public const string Blue = "blue";
    public const string Magenta = "magenta";
    public const string Cyan = "cyan";
    public const string White = "white";

    // -- Gray variants --
    public const string Gray = "gray";
    public const string Grey = "grey";
    public const string DimGray = "dim_gray";
    public const string DimGrey = "dim_grey";
    public const string DarkGray = "dark_gray";
    public const string DarkGrey = "dark_grey";
    public const string LightGray = "light_gray";
    public const string LightGrey = "light_grey";

    // -- Extended 256-color palette --
    public const string Orange = "orange";
    public const string Pink = "pink";
    public const string Purple = "purple";
    public const string Brown = "brown";
    public const string Lime = "lime";
    public const string Teal = "teal";
    public const string Gold = "gold";
    public const string Coral = "coral";
    public const string Salmon = "salmon";
    public const string SkyBlue = "sky_blue";
    public const string Olive = "olive";
    public const string Crimson = "crimson";
    public const string Slate = "slate";
    public const string Indigo = "indigo";

    // -- Theme-specific named colors --
    public const string Aquamarine = "aquamarine";
    public const string CadetBlue = "cadet_blue";
    public const string CornflowerBlue = "cornflower_blue";
    public const string DarkCyan = "dark_cyan";
    public const string DarkRed = "dark_red";
    public const string DarkSlateGray = "dark_slate_gray";
    public const string IndianRed = "indian_red";
    public const string LightBlue = "light_blue";
    public const string LightCoral = "light_coral";
    public const string LightGreen = "light_green";
    public const string LightSteelBlue = "light_steel_blue";
    public const string OrangeRed = "orange_red";
    public const string Silver = "silver";
    public const string SteelBlue = "steel_blue";

    // -- Dynamic format prefixes (not constants, but documented patterns) --
    // "ansi256:208"     -> 256-color index (0-255)
    // "#FF6B35"         -> RGB hex
    // "rgb:255,107,53"  -> RGB decimal
}
