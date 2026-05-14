#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using MMP.Herald.Pooling;
using MMP.Herald.Services;

namespace MMP.Herald.Output.Rich;

/// <summary>
/// ANSI escape code console writer. Renders styled log fragments with color,
/// bold, and italic using standard ANSI sequences supported by modern terminals.
///
/// Delegates color resolution to AnsiColorResolver (in Services/).
/// </summary>
public sealed class AnsiConsoleWriter : IRenderedLogOutputWriter
{
    private const string Reset = "\x1b[0m";
    private const string Bold = "\x1b[1m";
    private const string Italic = "\x1b[3m";

    private readonly AnsiColorResolver _colorResolver;

    public AnsiConsoleWriter() {
        _colorResolver = new AnsiColorResolver();
    }

    public AnsiConsoleWriter(IReadOnlyDictionary<string, int> customColors) {
        _colorResolver = new AnsiColorResolver(customColors);
    }

    public void RegisterColor(string name, int ansi256Index) =>
        _colorResolver.RegisterColor(name, ansi256Index);

    public void RegisterColor(string name, int red, int green, int blue) =>
        _colorResolver.RegisterColor(name, red, green, blue);

    public void Write(RenderedLogOutput output) {
        ArgumentNullException.ThrowIfNull(output);

        var builder = StringBuilderPool.Rent();

        foreach (var fragment in output.Fragments)
        {
            AppendFragment(builder, fragment);
        }

        Console.WriteLine(StringBuilderPool.ReturnAndGetString(builder));
    }

    private void AppendFragment(StringBuilder builder, RenderedLogFragment fragment) {
        var hasStyle = false;

        if (!string.IsNullOrWhiteSpace(fragment.BackgroundColorName))
        {
            var bgCode = _colorResolver.ResolveBackground(fragment.BackgroundColorName);
            if (bgCode is not null)
            {
                builder.Append(bgCode);
                hasStyle = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(fragment.ColorName))
        {
            var fgCode = _colorResolver.ResolveForeground(fragment.ColorName);
            if (fgCode is not null)
            {
                builder.Append(fgCode);
                hasStyle = true;
            }
        }

        if (fragment.IsBold)
        {
            builder.Append(Bold);
            hasStyle = true;
        }

        if (fragment.IsItalic)
        {
            builder.Append(Italic);
            hasStyle = true;
        }

        builder.Append(fragment.Text);

        if (hasStyle)
        {
            builder.Append(Reset);
        }
    }
}
