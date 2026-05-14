#nullable enable

using System;

namespace MMP.Herald.Output.Rich;
/// <summary>
/// Default host-neutral rich console writer.
/// It degrades rich fragments to plain text.
/// </summary>
public sealed class DefaultRichConsoleWriter : IRenderedLogOutputWriter
{
    public void Write(RenderedLogOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        Console.WriteLine(output.ToPlainText());
    }
}