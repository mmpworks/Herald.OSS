#nullable enable

using System;
using MMP.Herald.Output.Rich;

namespace MMP.Herald.Output.Writers;

/// <summary>
/// Adapts a file output to the IRenderedLogOutputWriter interface.
/// Used by channels that write to files. Converts RenderedLogOutput
/// fragments to plain text and writes each as a line.
/// </summary>
public sealed class FileOutputWriter : IRenderedLogOutputWriter, IDisposable
{
    private readonly ILineWriter _lineWriter;

    public FileOutputWriter(ILineWriter lineWriter)
    {
        _lineWriter = lineWriter ?? throw new ArgumentNullException(nameof(lineWriter));
    }

    public FileOutputWriter(string filePath)
        : this(new FileLineWriter(filePath))
    {
    }

    public void Write(RenderedLogOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var text = output.ToPlainText();
        if (!string.IsNullOrEmpty(text))
        {
            _lineWriter.WriteLine(text);
        }
    }

    /// <summary>
    /// Forwards disposal to the underlying writer when it is disposable so
    /// the long-lived <see cref="System.IO.FileStream"/> in
    /// <see cref="FileLineWriter"/> / <see cref="RollingFileLineWriter"/>
    /// gets flushed and released.
    /// </summary>
    public void Dispose()
    {
        if (_lineWriter is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
