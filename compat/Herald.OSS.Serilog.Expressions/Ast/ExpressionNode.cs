#nullable enable

using System.Collections.Generic;

namespace Herald.OSS.Serilog.Expressions.Ast;

/// <summary>
/// Abstract AST node for a parsed Serilog expression.
///
/// <para>
/// Unlike the boolean-only Query DSL AST (<c>QueryExpression</c>), every node
/// here is <b>value-producing</b>: evaluation yields an <see cref="object"/>?
/// (which may be the <see cref="Eval.Undefined"/> sentinel). The top-level
/// node's value is coerced to <see cref="bool"/> at the filter boundary. This
/// is what lets the grammar carry arithmetic, ternaries, function calls, and
/// <c>in</c>/<c>like</c> as first-class sub-expressions rather than only
/// comparisons.
/// </para>
///
/// <para>
/// Concrete nodes are sealed records so the evaluator dispatches with a
/// <c>switch</c> on node type — the same shape as <c>QueryEvaluator</c>,
/// new node set. No <c>System.Linq.Expressions.Compile</c>: the tree walk
/// is the AOT-clean default.
/// </para>
/// </summary>
internal abstract record ExpressionNode;

/// <summary>A literal value: string, number (double), boolean, or null.</summary>
internal sealed record LiteralNode(object? Value) : ExpressionNode;

/// <summary>An array literal — <c>[1, 2, 'x']</c>. Element nodes evaluate lazily.</summary>
internal sealed record ArrayNode(IReadOnlyList<ExpressionNode> Elements) : ExpressionNode;

/// <summary>
/// A built-in accessor: <c>@Level</c>, <c>@Message</c>, <c>@Timestamp</c>,
/// <c>@Exception</c>, <c>@Properties</c>, <c>@MessageTemplate</c>.
/// </summary>
internal sealed record BuiltinAccessorNode(BuiltinAccessor Accessor) : ExpressionNode;

/// <summary>
/// A bare property reference or dotted path — <c>RequestPath</c>,
/// <c>User.Id</c>. Resolves through <c>LogEvent.GetProperty</c>; a missing
/// property yields <see cref="Eval.Undefined"/> (not null).
/// </summary>
internal sealed record PropertyNode(IReadOnlyList<string> Path) : ExpressionNode;

/// <summary>
/// An indexer applied to a target — <c>@Properties['key']</c>,
/// <c>x['a']</c>. The index node evaluates to the key.
/// </summary>
internal sealed record IndexerNode(ExpressionNode Target, ExpressionNode Index) : ExpressionNode;

/// <summary>Unary minus — <c>-x</c>.</summary>
internal sealed record NegateNode(ExpressionNode Operand) : ExpressionNode;

/// <summary>Logical <c>not</c> — follows Kleene three-valued rules.</summary>
internal sealed record NotNode(ExpressionNode Operand) : ExpressionNode;

/// <summary>Binary operation — arithmetic, comparison, like/in, and/or.</summary>
internal sealed record BinaryNode(
    BinaryOperator Operator,
    ExpressionNode Left,
    ExpressionNode Right,
    bool CaseInsensitive = false) : ExpressionNode;

/// <summary>
/// SQL-style <c>like</c> / <c>not like</c>. The wildcard pattern compiles to a
/// matcher <b>once at config time</b> (stored here), never per event.
/// </summary>
internal sealed record LikeNode(
    ExpressionNode Target,
    Eval.LikeMatcher Matcher,
    bool Negated) : ExpressionNode;

/// <summary><c>x in [a, b, c]</c> — membership against an array of candidates.</summary>
internal sealed record InNode(
    ExpressionNode Target,
    IReadOnlyList<ExpressionNode> Candidates,
    bool CaseInsensitive = false) : ExpressionNode;

/// <summary><c>x is null</c> / <c>x is not null</c>.</summary>
internal sealed record IsNullNode(ExpressionNode Operand, bool Negated) : ExpressionNode;

/// <summary>Coalesce — <c>a ?? b</c> (and the <c>Coalesce(a, b)</c> builtin maps here).</summary>
internal sealed record CoalesceNode(ExpressionNode Left, ExpressionNode Right) : ExpressionNode;

/// <summary>Ternary — <c>cond ? whenTrue : whenFalse</c>.</summary>
internal sealed record ConditionalNode(
    ExpressionNode Condition,
    ExpressionNode WhenTrue,
    ExpressionNode WhenFalse) : ExpressionNode;

/// <summary>
/// A built-in function call. The function is resolved to a delegate at
/// <b>parse</b> time (unknown name → parse failure), so per-event dispatch is
/// a direct invoke with no name lookup.
/// </summary>
internal sealed record FunctionCallNode(
    string Name,
    Eval.BuiltinFunction Function,
    IReadOnlyList<ExpressionNode> Arguments) : ExpressionNode;

/// <summary>Built-in <c>@</c>-accessors over the Herald event.</summary>
internal enum BuiltinAccessor
{
    Level,
    Message,
    MessageTemplate,
    Timestamp,
    Exception,
    Properties,
}

/// <summary>Binary operators. Comparison ops carry rank/coercion semantics in the evaluator.</summary>
internal enum BinaryOperator
{
    // Logical (Kleene three-valued)
    And,
    Or,

    // Equality / comparison
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,

    // Arithmetic
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
}
