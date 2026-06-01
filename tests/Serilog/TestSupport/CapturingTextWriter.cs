#nullable enable

using System.IO;
using System.Text;

namespace MMP.Herald.OSS.Tests.Serilog.TestSupport;

/// <summary>
/// A <see cref="TextWriter"/> that captures everything written to it, so a W4
/// theme test can assert on the exact bytes a console formatter emits —
/// including ANSI escape codes — WITHOUT touching the global
/// <see cref="System.Console.Out"/>. Reading the captured string is reading the
/// emitted artifact: the test sees the literal ESC sequences (or their absence),
/// not the theme/config object that produced them.
/// </summary>
public sealed class CapturingTextWriter : TextWriter
{
    private readonly StringBuilder _buffer = new();

    public override Encoding Encoding => Encoding.UTF8;

    /// <summary>The full captured output, escape codes and all.</summary>
    public string Captured => _buffer.ToString();

    /// <summary>True when at least one ANSI CSI escape (<c>ESC[</c>) was written.</summary>
    public bool ContainsAnsiEscape => _buffer.ToString().Contains('\x1b');

    public override void Write(char value) => _buffer.Append(value);

    public override void Write(string? value) => _buffer.Append(value);
}
