#nullable enable

using MMP.Herald.Levels;

namespace MMP.Herald.Predicates;
/// <summary>
/// Compiles a data-driven predicate spec into an executable predicate.
/// </summary>
public interface ILogPredicateCompiler
{
    ILogPredicate Compile(PredicateSpec spec, ILogLevelRegistry levelRegistry);
}