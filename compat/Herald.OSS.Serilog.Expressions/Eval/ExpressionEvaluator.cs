#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Herald.OSS.Serilog.Expressions.Ast;
using MMP.Herald.Events;

namespace Herald.OSS.Serilog.Expressions.Eval;

/// <summary>
/// Tree-walks a cached <see cref="ExpressionNode"/> AST against a
/// <see cref="LogEvent"/> and returns the value the node produces.
///
/// <para>
/// Pure tree walk, AOT-clean: a <c>switch</c> on node type, no
/// <c>System.Linq.Expressions.Compile</c>, no reflection. The string is parsed
/// once at config; this runs per event. <c>like</c> patterns were already
/// compiled to a <see cref="LikeMatcher"/> at parse time and are reused here.
/// </para>
///
/// <para>
/// Three-valued: every helper may return <see cref="Undefined.Value"/>, which
/// propagates rather than collapsing to null or false. The top-level boolean
/// coercion (the only place undefined becomes false) lives in the filter, not
/// here.
/// </para>
/// </summary>
internal static class ExpressionEvaluator
{
    /// <summary>Evaluate <paramref name="node"/> against <paramref name="evt"/>.</summary>
    public static object? Evaluate(ExpressionNode node, LogEvent evt) => node switch
    {
        LiteralNode n => n.Value,
        ArrayNode n => n.Elements.Select(e => Evaluate(e, evt)).ToArray(),
        BuiltinAccessorNode n => ResolveAccessor(n.Accessor, evt),
        PropertyNode n => ResolveProperty(n.Path, evt),
        IndexerNode n => ResolveIndexer(n, evt),
        NegateNode n => Negate(Evaluate(n.Operand, evt)),
        NotNode n => Not(Evaluate(n.Operand, evt)),
        IsNullNode n => EvalIsNull(n, evt),
        CoalesceNode n => EvalCoalesce(n, evt),
        ConditionalNode n => EvalConditional(n, evt),
        LikeNode n => EvalLike(n, evt),
        InNode n => EvalIn(n, evt),
        FunctionCallNode n => n.Function(n.Arguments.Select(a => Evaluate(a, evt)).ToArray()),
        BinaryNode n => EvalBinary(n, evt),
        _ => Undefined.Value,
    };

    // --- accessors ---------------------------------------------------------

    private static object? ResolveAccessor(BuiltinAccessor accessor, LogEvent evt) => accessor switch
    {
        // Level resolves to the level key (string). F1 comparisons route this
        // through SerilogLevelMap rank, not a string compare. See EvalBinary.
        BuiltinAccessor.Level => evt.Level.Key,
        BuiltinAccessor.Message => evt.Message,
        BuiltinAccessor.MessageTemplate => evt.MessageTemplate,
        BuiltinAccessor.Timestamp => evt.TimeUtc,
        BuiltinAccessor.Exception => ResolveException(evt),
        // @Properties on its own is rarely compared directly; expose the event
        // so an indexer can resolve @Properties['x'].
        BuiltinAccessor.Properties => evt,
        _ => Undefined.Value,
    };

    // The exception text lives in the CausedBy field on the Herald event; absent
    // → undefined (Serilog's @x is undefined when there is no exception).
    private static object? ResolveException(LogEvent evt) =>
        evt.CausedBy is { } text ? text : Undefined.Value;

    private static object? ResolveProperty(IReadOnlyList<string> path, LogEvent evt)
    {
        var name = path.Count == 1 ? path[0] : string.Join(".", path);
        var prop = evt.GetProperty(name);
        // Missing property → undefined (distinct from a present-but-null value).
        return prop is { } p ? p.Value : Undefined.Value;
    }

    private static object? ResolveIndexer(IndexerNode node, LogEvent evt)
    {
        var target = Evaluate(node.Target, evt);
        var key = Evaluate(node.Index, evt);

        // @Properties['x'] — target is the event itself.
        if (target is LogEvent e && key is string propName)
        {
            var prop = e.GetProperty(propName);
            return prop is { } p ? p.Value : Undefined.Value;
        }

        // Generic list indexing — x[0].
        if (target is System.Collections.IList list && Coerce.TryAsDouble(key, out var idxD))
        {
            var idx = (int)idxD;
            return idx >= 0 && idx < list.Count ? list[idx] : Undefined.Value;
        }

        return Undefined.Value;
    }

    // --- unary -------------------------------------------------------------

    private static object? Negate(object? value) =>
        Coerce.TryAsDouble(value, out var d) ? -d : Undefined.Value;

    // Kleene NOT: not(true)=false, not(false)=true, not(undefined)=undefined.
    private static object? Not(object? value) => Coerce.Truthiness(value) switch
    {
        true => false,
        false => true,
        null => Undefined.Value,
    };

    // --- is null -----------------------------------------------------------

    private static object? EvalIsNull(IsNullNode node, LogEvent evt)
    {
        var value = Evaluate(node.Operand, evt);
        // 'is null' tests CLR null specifically; undefined is not null.
        var isNull = value is null;
        return node.Negated ? !isNull : isNull;
    }

    // --- coalesce ----------------------------------------------------------

    private static object? EvalCoalesce(CoalesceNode node, LogEvent evt)
    {
        var left = Evaluate(node.Left, evt);
        // ?? falls through on undefined OR null to the right operand.
        if (!Coerce.IsUndefined(left) && left is not null)
            return left;
        return Evaluate(node.Right, evt);
    }

    // --- ternary -----------------------------------------------------------

    private static object? EvalConditional(ConditionalNode node, LogEvent evt)
    {
        var condition = Coerce.Truthiness(Evaluate(node.Condition, evt));
        return condition switch
        {
            true => Evaluate(node.WhenTrue, evt),
            false => Evaluate(node.WhenFalse, evt),
            // Indeterminate condition → undefined, not an arbitrary branch.
            null => Undefined.Value,
        };
    }

    // --- like --------------------------------------------------------------

    private static object? EvalLike(LikeNode node, LogEvent evt)
    {
        var target = Evaluate(node.Target, evt);
        if (Coerce.IsUndefined(target) || target is null)
            return Undefined.Value;

        var matched = node.Matcher.IsMatch(Coerce.AsString(target));
        return node.Negated ? !matched : matched;
    }

    // --- in ----------------------------------------------------------------

    private static object? EvalIn(InNode node, LogEvent evt)
    {
        var target = Evaluate(node.Target, evt);
        if (Coerce.IsUndefined(target))
            return Undefined.Value;

        foreach (var candidateNode in node.Candidates)
        {
            var candidate = Evaluate(candidateNode, evt);
            if (Coerce.Equal(target, candidate, node.CaseInsensitive) == true)
                return true;
        }
        return false;
    }

    // --- binary ------------------------------------------------------------

    private static object? EvalBinary(BinaryNode node, LogEvent evt)
    {
        // Logical operators short-circuit with Kleene semantics — evaluate the
        // right operand only when needed, and never collapse undefined to false.
        if (node.Operator == BinaryOperator.And)
            return KleeneAnd(node, evt);
        if (node.Operator == BinaryOperator.Or)
            return KleeneOr(node, evt);

        var left = Evaluate(node.Left, evt);
        var right = Evaluate(node.Right, evt);

        // F1 — level-rank binding is keyed on the @Level ACCESSOR appearing in
        // the expression, not on a runtime string that merely looks like a
        // level name. That keeps '@Properties['k'] = 'Error'' a plain (case-
        // sensitive) string compare while '@Level = 'Error'' / '@Level >=
        // 'Warning'' compare by severity rank.
        var levelComparison = IsLevelOperand(node.Left) || IsLevelOperand(node.Right);

        return node.Operator switch
        {
            BinaryOperator.Equal => Boxed(EqualOp(left, right, node.CaseInsensitive, levelComparison)),
            BinaryOperator.NotEqual => Boxed(NegateNullable(EqualOp(left, right, node.CaseInsensitive, levelComparison))),
            BinaryOperator.Less => CompareOp(left, right, node.CaseInsensitive, levelComparison, static c => c < 0),
            BinaryOperator.LessOrEqual => CompareOp(left, right, node.CaseInsensitive, levelComparison, static c => c <= 0),
            BinaryOperator.Greater => CompareOp(left, right, node.CaseInsensitive, levelComparison, static c => c > 0),
            BinaryOperator.GreaterOrEqual => CompareOp(left, right, node.CaseInsensitive, levelComparison, static c => c >= 0),
            BinaryOperator.Add => Arith(left, right, static (l, r) => l + r),
            BinaryOperator.Subtract => Arith(left, right, static (l, r) => l - r),
            BinaryOperator.Multiply => Arith(left, right, static (l, r) => l * r),
            BinaryOperator.Divide => Divide(left, right),
            BinaryOperator.Modulo => Modulo(left, right),
            _ => Undefined.Value,
        };
    }

    private static object? KleeneAnd(BinaryNode node, LogEvent evt)
    {
        var left = Coerce.Truthiness(Evaluate(node.Left, evt));
        if (left == false) return false;                       // false and X = false
        var right = Coerce.Truthiness(Evaluate(node.Right, evt));
        if (right == false) return false;
        if (left == true && right == true) return true;
        return Undefined.Value;                                // any indeterminate remains
    }

    private static object? KleeneOr(BinaryNode node, LogEvent evt)
    {
        var left = Coerce.Truthiness(Evaluate(node.Left, evt));
        if (left == true) return true;                         // true or X = true
        var right = Coerce.Truthiness(Evaluate(node.Right, evt));
        if (right == true) return true;
        if (left == false && right == false) return false;
        return Undefined.Value;
    }

    // True when a node is the @Level accessor — the operand that triggers the
    // F1 severity-rank binding.
    private static bool IsLevelOperand(ExpressionNode node) =>
        node is BuiltinAccessorNode { Accessor: BuiltinAccessor.Level };

    // Comparison op with the F1 @Level rank rule: when a @Level operand is
    // present, compare by severity rank, not lexically.
    private static object? CompareOp(object? left, object? right, bool ci, bool levelComparison, Func<int, bool> test)
    {
        int? comparison = levelComparison
            ? Coerce.CompareLevelRank(left, right)
            : Coerce.Compare(left, right, ci);

        return comparison is { } c ? test(c) : (object)Undefined.Value;
    }

    // Equality with the same F1 binding: when a @Level operand is present,
    // equate by resolved level identity so '@Level = 'Error'' matches whether
    // the runtime key is 'error' and the literal is the 'Error' display name. A
    // plain string compare would silently miss on the key/display-name casing
    // mismatch — the same class of bug as the ordering trap.
    private static bool? EqualOp(object? left, object? right, bool ci, bool levelComparison)
    {
        // @Level equality compares by resolved level IDENTITY, not rank: a
        // Herald-only level with no Serilog ordinal still equals itself, where a
        // rank compare would yield null. Rank ops keep the ordinal path.
        if (levelComparison)
            return Coerce.EqualLevelIdentity(left, right);
        return Coerce.Equal(left, right, ci);
    }

    private static object? Arith(object? left, object? right, Func<double, double, double> op) =>
        Coerce.TryBothAsDouble(left, right, out var l, out var r) ? op(l, r) : Undefined.Value;

    private static object? Divide(object? left, object? right)
    {
        if (!Coerce.TryBothAsDouble(left, right, out var l, out var r) || r == 0)
            return Undefined.Value;
        return l / r;
    }

    private static object? Modulo(object? left, object? right)
    {
        if (!Coerce.TryBothAsDouble(left, right, out var l, out var r) || r == 0)
            return Undefined.Value;
        return l % r;
    }

    // Box a Kleene comparison result: a definite bool, or undefined when the
    // comparison was indeterminate.
    private static object Boxed(bool? value) => value is { } b ? b : (object)Undefined.Value;

    private static bool? NegateNullable(bool? value) => value is { } b ? !b : null;
}
