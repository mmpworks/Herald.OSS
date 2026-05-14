#nullable enable

using System;
using MMP.Herald.Events;

namespace MMP.Herald.Predicates;
/// <summary>
/// Small adapter that turns a delegate into an <see cref="ILogPredicate"/>.
/// </summary>
public sealed class CompiledLogPredicate : ILogPredicate
{
    private readonly Func<LogEvent, bool> _evaluator;

    public CompiledLogPredicate(Func<LogEvent, bool> evaluator)
    {
        _evaluator = evaluator;
    }

    public bool Evaluate(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        return _evaluator(logEvent);
    }
}