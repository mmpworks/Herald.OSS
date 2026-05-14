#nullable enable

using System;
using MMP.Herald.Events;
using MMP.Herald.Filters;

namespace MMP.Herald.Predicates;
/// <summary>
/// Adapter that turns a predicate into an ILogFilter.
/// </summary>
public sealed class PredicateFilter : ILogFilter
{
    private readonly Func<LogEvent, bool> _predicate;

    public PredicateFilter(Func<LogEvent, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _predicate = predicate;
    }

    public bool Allow(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        return _predicate(logEvent);
    }
}