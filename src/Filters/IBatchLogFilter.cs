#nullable enable

using System.Collections.Generic;
using MMP.Herald.Events;

namespace MMP.Herald.Filters;

/// <summary>
/// Batch-aware filter that examines an entire batch of events before deciding
/// which events survive. Distinct from ILogFilter which evaluates single events.
/// </summary>
public interface IBatchLogFilter
{
    IReadOnlyList<LogEvent> FilterBatch(IReadOnlyList<LogEvent> batch);
}
