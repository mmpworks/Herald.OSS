#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Output.Rendering.Themes;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Sinks.SystemConsole.Themes;

/// <summary>
/// Serilog-name console theme driven by <see cref="System.ConsoleColor"/> styles.
/// Mirrors <c>Serilog.Sinks.SystemConsole.Themes.SystemConsoleTheme</c>: the named
/// accessors (<see cref="Literate"/>, <see cref="Colored"/>, <see cref="Grayscale"/>)
/// map to Herald's built-in engine themes, and the public constructor takes a custom
/// style map so a migrated <c>new SystemConsoleTheme(new Dictionary&lt;ConsoleThemeStyle,
/// SystemConsoleThemeStyle&gt;{...})</c> palette compiles and renders.
///
/// <para>
/// A custom theme's per-level <see cref="System.ConsoleColor"/> foreground is
/// translated to the matching ANSI escape so the operator's chosen level colours
/// render on Herald's themed-console path. Background colour and non-level members
/// are accepted for source parity; the level foreground is what drives the line.
/// </para>
/// </summary>
public sealed class SystemConsoleTheme : ConsoleTheme
{
    private readonly IConsoleTheme _engineTheme;
    private readonly IReadOnlyDictionary<LogEventLevel, string> _levelEscapes;

    /// <summary>
    /// Construct a custom system-console theme from a
    /// <see cref="ConsoleThemeStyle"/>-to-<see cref="SystemConsoleThemeStyle"/> map.
    /// Mirrors Serilog's custom-theme constructor.
    /// </summary>
    /// <param name="styles">Style-to-colour map. Must not be null.</param>
    public SystemConsoleTheme(IReadOnlyDictionary<ConsoleThemeStyle, SystemConsoleThemeStyle> styles)
    {
        ArgumentNullException.ThrowIfNull(styles);
        _engineTheme = Unstyled;
        _levelEscapes = BuildLevelEscapes(styles);
    }

    private SystemConsoleTheme(IConsoleTheme engineTheme)
    {
        _engineTheme = engineTheme;
        _levelEscapes = EmptyEscapes;
    }

    private static readonly IReadOnlyDictionary<LogEventLevel, string> EmptyEscapes
        = new Dictionary<LogEventLevel, string>();

    /// <summary>Colourful, readable theme — maps to Herald's Literate engine theme.</summary>
    public static SystemConsoleTheme Literate { get; } = new(BuiltInConsoleThemes.Literate);

    /// <summary>Strong-contrast coloured theme — maps to Herald's Light engine theme.</summary>
    public static SystemConsoleTheme Colored { get; } = new(BuiltInConsoleThemes.Light);

    /// <summary>No colour — maps to Herald's unstyled engine theme (byte-identical to unthemed).</summary>
    public static SystemConsoleTheme Grayscale { get; } = new(BuiltInConsoleThemes.None);

    /// <inheritdoc/>
    public override OutputStyle? Resolve(OutputElementKind element, string? qualifier = null)
        => _engineTheme.Resolve(element, qualifier);

    /// <inheritdoc/>
    public override bool TryGetLevelEscape(LogEventLevel level, out string? escape)
    {
        if (_levelEscapes.TryGetValue(level, out var raw) && !string.IsNullOrEmpty(raw))
        {
            escape = raw;
            return true;
        }
        escape = null;
        return false;
    }

    private static IReadOnlyDictionary<LogEventLevel, string> BuildLevelEscapes(
        IReadOnlyDictionary<ConsoleThemeStyle, SystemConsoleThemeStyle> styles)
    {
        var map = new Dictionary<LogEventLevel, string>();
        AddIfPresent(styles, map, ConsoleThemeStyle.LevelVerbose, LogEventLevel.Verbose);
        AddIfPresent(styles, map, ConsoleThemeStyle.LevelDebug, LogEventLevel.Debug);
        AddIfPresent(styles, map, ConsoleThemeStyle.LevelInformation, LogEventLevel.Information);
        AddIfPresent(styles, map, ConsoleThemeStyle.LevelWarning, LogEventLevel.Warning);
        AddIfPresent(styles, map, ConsoleThemeStyle.LevelError, LogEventLevel.Error);
        AddIfPresent(styles, map, ConsoleThemeStyle.LevelFatal, LogEventLevel.Fatal);
        return map;
    }

    private static void AddIfPresent(
        IReadOnlyDictionary<ConsoleThemeStyle, SystemConsoleThemeStyle> styles,
        Dictionary<LogEventLevel, string> map,
        ConsoleThemeStyle style,
        LogEventLevel level)
    {
        if (styles.TryGetValue(style, out var s))
        {
            var escape = SystemConsoleColorToAnsi.Foreground(s.Foreground);
            if (!string.IsNullOrEmpty(escape))
                map[level] = escape!;
        }
    }
}
