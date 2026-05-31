#nullable enable

// Layer-2 mirror of Serilog.Formatting.ITextFormatter.
// Uses Serilog.Events.LogEvent (L2 wrapper type) so consumer formatter classes
// see the correct type in their Format() method signature.
//
// Layer-1 twin: MMP.Herald.Serilog.Formatting.ITextFormatter

using System.IO;

namespace Serilog.Formatting;

/// <summary>
/// Formats a <see cref="Events.LogEvent"/> (L2 Serilog mirror) to a <see cref="TextWriter"/>.
/// Exported under the <c>Serilog</c> assembly identity so formatter implementations
/// compiled against the real Serilog package compile unchanged after the swap.
/// </summary>
public interface ITextFormatter
{
    /// <summary>Format <paramref name="logEvent"/> and write the result to <paramref name="output"/>.</summary>
    void Format(Events.LogEvent logEvent, TextWriter output);
}
