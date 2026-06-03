#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Output.Rendering.Themes;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Sinks.SystemConsole.Themes;

/// <summary>
/// Serilog-name console theme driven by raw ANSI escape sequences. Mirrors
/// <c>Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme</c>: the named
/// accessors (<see cref="Literate"/>, <see cref="Code"/>, <see cref="Grayscale"/>,
/// <see cref="Sixteen"/>) map to Herald's built-in engine themes, and the public
/// constructor takes a custom <see cref="ConsoleThemeStyle"/>-to-escape map so a
/// migrated <c>new AnsiConsoleTheme(new Dictionary&lt;ConsoleThemeStyle,string&gt;{...})</c>
/// palette renders the operator's exact colours.
///
/// <para>
/// A custom theme's level escapes (<see cref="ConsoleThemeStyle.LevelVerbose"/>..
/// <see cref="ConsoleThemeStyle.LevelFatal"/>) drive the per-line styling Herald's
/// themed-console path applies; non-level entries are accepted for source parity.
/// </para>
/// </summary>
public sealed class AnsiConsoleTheme : ConsoleTheme
{
    // Named-built-in mode: styling delegates to this engine theme; no raw escape.
    // Custom mode: this is the unstyled engine theme and _levelEscapes carries the
    // operator's literal sequences.
    private readonly IConsoleTheme _engineTheme;

    // Per-level raw ANSI escapes, keyed by the Serilog LogEventLevel mapped from
    // the ConsoleThemeStyle.Level* member. Empty for named built-ins.
    private readonly IReadOnlyDictionary<LogEventLevel, string> _levelEscapes;

    /// <summary>
    /// Construct a custom ANSI theme from a <see cref="ConsoleThemeStyle"/>-to-escape
    /// map. Each value is a literal ANSI escape string emitted verbatim before a line
    /// at the matching level. Mirrors Serilog's custom-theme constructor.
    /// </summary>
    /// <param name="styles">Style-to-ANSI-escape map. Must not be null.</param>
    public AnsiConsoleTheme(IReadOnlyDictionary<ConsoleThemeStyle, string> styles)
    {
        ArgumentNullException.ThrowIfNull(styles);
        _engineTheme = Unstyled;
        _levelEscapes = BuildLevelEscapes(styles);
    }

    // Named-built-in ctor: wrap an engine theme, no custom escapes.
    private AnsiConsoleTheme(IConsoleTheme engineTheme)
    {
        _engineTheme = engineTheme;
        _levelEscapes = EmptyEscapes;
    }

    private static readonly IReadOnlyDictionary<LogEventLevel, string> EmptyEscapes
        = new Dictionary<LogEventLevel, string>();

    /// <summary>Colourful, readable theme — maps to Herald's Literate engine theme.</summary>
    public static AnsiConsoleTheme Literate { get; } = new(BuiltInConsoleThemes.Literate);

    /// <summary>Muted dark-background theme — maps to Herald's Dark engine theme.</summary>
    public static AnsiConsoleTheme Code { get; } = new(BuiltInConsoleThemes.Dark);

    /// <summary>No colour — maps to Herald's unstyled engine theme (byte-identical to unthemed).</summary>
    public static AnsiConsoleTheme Grayscale { get; } = new(BuiltInConsoleThemes.None);

    /// <summary>Sixteen-colour theme — maps to Herald's Literate engine theme (closest built-in).</summary>
    public static AnsiConsoleTheme Sixteen { get; } = new(BuiltInConsoleThemes.Literate);

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

    // Project the ConsoleThemeStyle-keyed map down to the level escapes the
    // themed formatter consumes. Non-level members are intentionally dropped —
    // Herald's themed path styles the whole line off the event level today.
    private static IReadOnlyDictionary<LogEventLevel, string> BuildLevelEscapes(
        IReadOnlyDictionary<ConsoleThemeStyle, string> styles)
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
        IReadOnlyDictionary<ConsoleThemeStyle, string> styles,
        Dictionary<LogEventLevel, string> map,
        ConsoleThemeStyle style,
        LogEventLevel level)
    {
        if (styles.TryGetValue(style, out var escape) && !string.IsNullOrEmpty(escape))
            map[level] = escape;
    }
}
