#nullable enable

using System.Collections.Generic;
using MMP.Herald.Events;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Optional sink capability for writing a batch of events in one operation.
/// </summary>
public interface IBatchedLogSink
{
    void LogBatch(IReadOnlyList<LogEvent> events);
}