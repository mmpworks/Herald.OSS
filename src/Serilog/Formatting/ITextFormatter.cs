#nullable enable

using System.IO;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Formatting;

/// <summary>
/// Formats a <see cref="LogEvent"/> (Serilog mirror) to a <see cref="TextWriter"/>.
///
/// <para>
/// Mirrors the contract of <c>Serilog.Formatting.ITextFormatter</c> so output-sink
/// implementations written against the real Serilog surface compile unchanged against
/// MMP.Herald.Serilog.
/// </para>
/// </summary>
public interface ITextFormatter
{
    /// <summary>Format <paramref name="logEvent"/> and write the result to <paramref name="output"/>.</summary>
    void Format(LogEvent logEvent, TextWriter output);
}
