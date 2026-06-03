#nullable enable

using System;

namespace MMP.Herald.Serilog.Sinks.SystemConsole.Themes;

/// <summary>
/// Translates a <see cref="ConsoleColor"/> to the ANSI SGR foreground escape so a
/// <see cref="SystemConsoleTheme"/> custom palette renders on Herald's themed-console
/// path (which speaks ANSI). The 16 console colours map to the standard ANSI
/// 30-37 / 90-97 foreground codes — the same vocabulary
/// <see cref="MMP.Herald.Output.Rich.AnsiConsoleWriter"/> emits.
/// </summary>
internal static class SystemConsoleColorToAnsi
{
    // Standard ANSI foreground SGR codes for the 16 ConsoleColor values.
    // Dark colours use 30-37; bright colours use 90-97.
    public static string? Foreground(ConsoleColor color) => color switch
    {
        ConsoleColor.Black       => "\x1b[30m",
        ConsoleColor.DarkRed     => "\x1b[31m",
        ConsoleColor.DarkGreen   => "\x1b[32m",
        ConsoleColor.DarkYellow  => "\x1b[33m",
        ConsoleColor.DarkBlue    => "\x1b[34m",
        ConsoleColor.DarkMagenta => "\x1b[35m",
        ConsoleColor.DarkCyan    => "\x1b[36m",
        ConsoleColor.Gray        => "\x1b[37m",
        ConsoleColor.DarkGray    => "\x1b[90m",
        ConsoleColor.Red         => "\x1b[91m",
        ConsoleColor.Green       => "\x1b[92m",
        ConsoleColor.Yellow      => "\x1b[93m",
        ConsoleColor.Blue        => "\x1b[94m",
        ConsoleColor.Magenta     => "\x1b[95m",
        ConsoleColor.Cyan        => "\x1b[96m",
        ConsoleColor.White       => "\x1b[97m",
        _                        => null,
    };
}
