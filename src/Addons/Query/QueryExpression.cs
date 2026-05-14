#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Addons.Query;

/// <summary>
/// Abstract AST node for a parsed Herald query. Concrete nodes are sealed
/// records so evaluation uses pattern matching.
/// </summary>
public abstract record QueryExpression;

/// <summary>
/// Logical AND of two sub-expressions.
/// </summary>
public sealed record AndExpression(QueryExpression Left, QueryExpression Right) : QueryExpression;

/// <summary>
/// Logical OR of two sub-expressions.
/// </summary>
public sealed record OrExpression(QueryExpression Left, QueryExpression Right) : QueryExpression;

/// <summary>
/// Logical NOT of the inner expression.
/// </summary>
public sealed record NotExpression(QueryExpression Inner) : QueryExpression;

/// <summary>
/// Comparison between a field and a literal value.
///
/// <para>
/// <see cref="FieldPath"/> is a dotted identifier — <c>level</c>,
/// <c>category</c>, <c>message</c>, <c>template</c>, or
/// <c>property.&lt;name&gt;</c>. The evaluator resolves the path against a
/// <see cref="MMP.Herald.Events.LogEvent"/> at run time.
/// </para>
/// </summary>
public sealed record TermExpression(
    IReadOnlyList<string> FieldPath,
    QueryComparisonOperator Op,
    QueryValue Value) : QueryExpression;

/// <summary>
/// Comparison operators supported by <see cref="TermExpression"/>.
/// </summary>
public enum QueryComparisonOperator
{
    /// <summary>Natural match: substring (case-insensitive) for strings, equality for numbers.</summary>
    Match,
    /// <summary>Strict equals (case-insensitive for strings).</summary>
    Equals,
    /// <summary>Strict not equals (case-insensitive for strings).</summary>
    NotEquals,
    /// <summary>Regex match against the field rendered as a string.</summary>
    Regex,
    /// <summary>Numeric or lexicographic greater-than.</summary>
    Greater,
    /// <summary>Numeric or lexicographic greater-or-equal.</summary>
    GreaterOrEqual,
    /// <summary>Numeric or lexicographic less-than.</summary>
    Less,
    /// <summary>Numeric or lexicographic less-or-equal.</summary>
    LessOrEqual,
}

/// <summary>
/// A literal value captured by the parser. <see cref="Text"/> always holds
/// the raw string form; <see cref="IsString"/> indicates the token was quoted
/// so the evaluator skips numeric coercion.
/// </summary>
public sealed record QueryValue(string Text, bool IsString);
