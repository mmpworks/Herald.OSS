#nullable enable

using System;

namespace MMP.Herald.Serilog.Sinks.SystemConsole.Themes;

/// <summary>
/// A foreground/background <see cref="ConsoleColor"/> pair for one element of a
/// <see cref="SystemConsoleTheme"/>. Mirrors
/// <c>Serilog.Sinks.SystemConsole.Themes.SystemConsoleThemeStyle</c> so a migrated
/// custom <c>SystemConsoleTheme</c> map compiles with only the namespace find-replace.
/// </summary>
public struct SystemConsoleThemeStyle
{
    /// <summary>The foreground colour to apply.</summary>
    public ConsoleColor Foreground { get; set; }

    /// <summary>The background colour to apply, or <c>null</c> for the console default.</summary>
    public ConsoleColor? Background { get; set; }
}
