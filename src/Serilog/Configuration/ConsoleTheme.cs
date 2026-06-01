#nullable enable

using MMP.Herald.Output.Rendering.Themes;

namespace MMP.Herald.Serilog.Configuration;

/// <summary>
/// Serilog-name facade over Herald's built-in console themes. A migrated config
/// that names <c>AnsiConsoleTheme.Literate</c> / <c>SystemConsoleTheme.Colored</c>
/// reaches the equivalent Herald <see cref="IConsoleTheme"/> through these members,
/// so <c>WriteTo.Console(theme: ConsoleTheme.Literate)</c> compiles and colours the
/// same way.
///
/// <para>
/// Each member returns a shared engine theme instance — no allocation per call.
/// <see cref="None"/> and <see cref="Grayscale"/> both resolve to the unstyled
/// engine theme, which is what makes the "no theme == no ANSI" guard hold byte-for-byte.
/// </para>
/// </summary>
public static class ConsoleTheme
{
    /// <summary>Colourful, readable theme — maps to the engine's Literate theme.</summary>
    public static IConsoleTheme Literate => BuiltInConsoleThemes.Literate;

    /// <summary>Muted dark-background theme — maps to the engine's Dark theme.</summary>
    public static IConsoleTheme Code => BuiltInConsoleThemes.Dark;

    /// <summary>No colour — maps to the engine's unstyled theme.</summary>
    public static IConsoleTheme Grayscale => BuiltInConsoleThemes.None;

    /// <summary>No colour — maps to the engine's unstyled theme (byte-identical to unthemed).</summary>
    public static IConsoleTheme None => BuiltInConsoleThemes.None;
}
