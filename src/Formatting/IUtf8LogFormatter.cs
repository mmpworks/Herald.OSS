#nullable enable

using System.Buffers;
using MMP.Herald.Events;

namespace MMP.Herald.Formatting;

/// <summary>
/// Formats log events directly to UTF-8 bytes without intermediate string allocation.
/// Sinks that support byte output should prefer this over ILogFormatter.Format()
/// to eliminate the string -> UTF-8 encoding step.
///
/// Usage:
///   if (formatter is IUtf8LogFormatter utf8)
///   {
///       utf8.Format(logEvent, bufferWriter);
///   }
///   else
///   {
///       var text = ((ILogFormatter)formatter).Format(logEvent);
///       // encode text to bytes
///   }
/// </summary>
public interface IUtf8LogFormatter
{
    void Format(LogEvent logEvent, IBufferWriter<byte> output);
}
