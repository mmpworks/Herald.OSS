#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Events;
using MMP.Herald.Predicates;

namespace MMP.Herald.Filters;

/// <summary>
/// Applies a list of sampling rules. Each rule has a scope predicate and either a
/// count-based sampler or a time-based throttler. The first matching rule applies;
/// events matching no rule pass through unsampled.
/// </summary>
public sealed class CompositeSamplingFilter : ILogFilter
{
    private readonly IReadOnlyList<SamplingRuleEntry> _rules;

    public CompositeSamplingFilter(IReadOnlyList<SamplingRuleEntry> rules)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
    }

    public bool Allow(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        foreach (var rule in _rules)
        {
            if (rule.Scope.Evaluate(logEvent))
            {
                return rule.Filter.Allow(logEvent);
            }
        }

        return true;
    }

    /// <summary>
    /// A sampling rule binding a scope predicate to a filter (SamplingFilter or ThrottlingFilter).
    /// </summary>
    public sealed record SamplingRuleEntry(ILogPredicate Scope, ILogFilter Filter);
}
