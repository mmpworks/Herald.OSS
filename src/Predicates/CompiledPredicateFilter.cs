#nullable enable

using System;
using MMP.Herald.Events;
using MMP.Herald.Filters;

namespace MMP.Herald.Predicates;
/// <summary>
/// Filter adapter that delegates to a compiled log predicate.
/// </summary>
public sealed class CompiledPredicateFilter : ILogFilter
{
    private readonly ILogPredicate _predicate;

    public CompiledPredicateFilter(ILogPredicate predicate)
    {
        _predicate = predicate;
    }

    public bool Allow(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        return _predicate.Evaluate(logEvent);
    }
}