#nullable enable

using MMP.Herald.Events;

namespace MMP.Herald.Predicates;
/// <summary>
/// Executable predicate compiled from a predicate spec.
/// </summary>
public interface ILogPredicate
{
    bool Evaluate(LogEvent logEvent);
}