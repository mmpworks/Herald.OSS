#nullable enable

using MMP.Herald.Events;

namespace MMP.Herald.Filters;
/// <summary>
/// Predicate-like boundary for event filtering.
/// </summary>
public interface ILogFilter
{
    bool Allow(LogEvent logEvent);
}