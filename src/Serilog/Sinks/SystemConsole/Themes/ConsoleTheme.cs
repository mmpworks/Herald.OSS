#nullable enable

using MMP.Herald.Output.Rendering.Themes;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Sinks.SystemConsole.Themes;

/// <summary>
/// Base class for the Serilog-shaped console themes a migrated config names —
/// <see cref="AnsiConsoleTheme"/> and <see cref="SystemConsoleTheme"/>. It is an
/// <see cref="IConsoleTheme"/> so it drops straight into Herald's existing
/// <c>WriteTo.Console(IConsoleTheme, ...)</c> overload, and it carries a literal
/// per-level ANSI escape so a CUSTOM palette renders the operator's exact colours
/// rather than being remapped through Herald's colour-name resolver.
///
/// <para>
/// <b>Two render modes, one type.</b> A theme built from a custom style map
/// supplies a raw ANSI escape string per <see cref="ConsoleThemeStyle"/> level
/// member; <see cref="TryGetLevelEscape"/> returns it and the themed formatter
/// emits it verbatim. A named built-in (e.g. <see cref="AnsiConsoleTheme.Code"/>)
/// delegates styling to a wrapped engine <see cref="IConsoleTheme"/> and returns
/// no literal escape, so the formatter keeps its existing colour-name path —
/// byte-identical to before this type existed.
/// </para>
/// </summary>
public abstract class ConsoleTheme : IConsoleTheme
{
    /// <summary>
    /// Resolve the engine-shaped style for a built-in theme. Custom-map themes
    /// return <c>null</c> here (their styling rides <see cref="TryGetLevelEscape"/>
    /// instead), which keeps <see cref="None"/>/unstyled output escape-free.
    /// </summary>
    public abstract OutputStyle? Resolve(OutputElementKind element, string? qualifier = null);

    /// <summary>
    /// Try to supply the literal ANSI escape sequence for the event's level.
    /// Returns <c>true</c> and the raw escape string when this theme carries a
    /// custom palette entry for the level; <c>false</c> when the level is
    /// unstyled or this is a name-based built-in theme (the formatter then falls
    /// back to its colour-name path).
    /// </summary>
    /// <param name="level">The Serilog level of the event being rendered.</param>
    /// <param name="escape">The raw ANSI escape to emit before the line, when present.</param>
    public abstract bool TryGetLevelEscape(LogEventLevel level, out string? escape);

    /// <summary>The reset sequence emitted after a styled line. Matches Serilog's <c>\x1b[0m</c>.</summary>
    public const string AnsiReset = "\x1b[0m";

    // No styling — every element unstyled, no level escape. Used as the shared
    // "byte-identical to unthemed" theme for AnsiConsoleTheme/SystemConsoleTheme
    // None/Grayscale accessors.
    internal static readonly IConsoleTheme Unstyled = BuiltInConsoleThemes.None;
}
