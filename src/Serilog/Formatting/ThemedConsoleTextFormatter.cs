#nullable enable

using System;
using System.IO;
using MMP.Herald.Output.Rendering.Themes;
using MMP.Herald.Serilog.Events;
using MMP.Herald.Services;

namespace MMP.Herald.Serilog.Formatting;

/// <summary>
/// An <see cref="ITextFormatter"/> that renders an event through an inner
/// output-template formatter and wraps the result in ANSI styling drawn from an
/// <see cref="IConsoleTheme"/>. This is the W4 themed-console path: it rides the
/// existing <c>WriteTo.Console(ITextFormatter)</c> seam (a formatter is a formatter)
/// but, unlike the net9-gated bridge, carries no kernel-buffer dependency, so it
/// compiles and runs on net8/net9/net10 alike.
///
/// <para>
/// <b>Theme application.</b> The event's level style (<c>LevelText</c> entry for the
/// event's level key) drives a single ANSI wrap around the whole rendered line:
/// background → foreground → bold → italic → text → reset. This is the same escape
/// vocabulary <see cref="MMP.Herald.Output.Rich.AnsiConsoleWriter"/> emits, so a
/// themed line here is consistent with Herald's rich console output.
/// </para>
///
/// <para>
/// <b>Guards (both produce byte-identical output to the unthemed inner formatter):</b>
/// <list type="bullet">
///   <item><description>
///     The theme resolves no style for the level (e.g. <see cref="ConsoleTheme.None"/>
///     / <see cref="ConsoleTheme.Grayscale"/>) → no escapes are written.
///   </description></item>
///   <item><description>
///     <see cref="System.Console.IsOutputRedirected"/> is <c>true</c> → no escapes are
///     written, so a redirected log file or pipe never carries raw ESC sequences.
///   </description></item>
/// </list>
/// </para>
/// </summary>
public sealed class ThemedConsoleTextFormatter : ITextFormatter
{
    // Same escape constants AnsiConsoleWriter uses, so themed output is byte-consistent.
    private const string Reset = "\x1b[0m";
    private const string Bold = "\x1b[1m";
    private const string Italic = "\x1b[3m";

    private readonly ITextFormatter _inner;
    private readonly IConsoleTheme _theme;
    private readonly AnsiColorResolver _colorResolver;

    // Resolved once at construction: when output is redirected we never emit ANSI,
    // matching Serilog's console-theme behaviour (a pipe/file gets clean text).
    private readonly bool _emitAnsi;

    /// <summary>
    /// Create a themed formatter. ANSI is suppressed automatically when the console
    /// output is redirected (<see cref="System.Console.IsOutputRedirected"/>).
    /// </summary>
    /// <param name="theme">The theme that supplies the per-level style.</param>
    /// <param name="outputTemplate">
    ///   The Serilog output-template string for the inner text rendering, or null
    ///   for <see cref="MessageTemplateTextFormatter.DefaultOutputTemplate"/>.
    /// </param>
    public ThemedConsoleTextFormatter(IConsoleTheme theme, string? outputTemplate = null)
        : this(theme, outputTemplate, emitAnsi: !Console.IsOutputRedirected)
    {
    }

    // Test seam: the redirect decision is injected so both branches (emit / suppress)
    // are exercisable without flipping the process-wide Console.IsOutputRedirected.
    internal ThemedConsoleTextFormatter(IConsoleTheme theme, string? outputTemplate, bool emitAnsi)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;
        _inner = new MessageTemplateTextFormatter(outputTemplate);
        _colorResolver = new AnsiColorResolver();
        _emitAnsi = emitAnsi;
    }

    /// <inheritdoc/>
    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(output);

        // Resolve the level style. No style, or suppressed ANSI (redirected output),
        // means the themed formatter is byte-identical to the inner formatter
        // (the None/redirect guard).
        var style = _emitAnsi ? ResolveLevelStyle(logEvent) : null;
        if (style is null)
        {
            _inner.Format(logEvent, output);
            return;
        }

        // Render the text body once, then wrap it in the level's ANSI styling.
        using var inner = new StringWriter();
        _inner.Format(logEvent, inner);
        WriteStyled(output, inner.ToString(), style);
    }

    // The LevelText style for the event's Herald level key, or null when the theme
    // leaves the level unstyled (None resolves null for every element).
    private OutputStyle? ResolveLevelStyle(LogEvent logEvent)
    {
        var levelKey = SerilogLevelMap.ToHerald(logEvent.Level).Key;
        return _theme.Resolve(OutputElementKind.LevelText.Instance, levelKey);
    }

    // background → foreground → bold → italic → text → reset.
    // Reset is emitted only when at least one style attribute was written, matching
    // AnsiConsoleWriter so an all-default style produces no stray reset.
    private void WriteStyled(TextWriter output, string text, OutputStyle style)
    {
        var styled = false;

        if (!string.IsNullOrWhiteSpace(style.BackgroundColorName))
        {
            var bg = _colorResolver.ResolveBackground(style.BackgroundColorName!);
            if (bg is not null) { output.Write(bg); styled = true; }
        }

        if (!string.IsNullOrWhiteSpace(style.ColorName))
        {
            var fg = _colorResolver.ResolveForeground(style.ColorName!);
            if (fg is not null) { output.Write(fg); styled = true; }
        }

        if (style.UseBold) { output.Write(Bold); styled = true; }
        if (style.UseItalic) { output.Write(Italic); styled = true; }

        output.Write(text);

        if (styled) output.Write(Reset);
    }
}
