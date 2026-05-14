#nullable enable

using System.Collections.Generic;
using MMP.Herald.Events;

namespace MMP.Herald.Serialization;

/// <summary>
/// General-purpose serialization interface for log events.
/// Generic over output type: byte[] for binary formats, string for text formats.
/// </summary>
public interface ILogEventSerializer<out T>
{
    /// <summary>
    /// Serialize a single log event.
    /// </summary>
    T Serialize(LogEvent logEvent);

    /// <summary>
    /// Serialize a batch of log events as a single payload.
    /// </summary>
    T SerializeBatch(IReadOnlyList<LogEvent> events);
}
