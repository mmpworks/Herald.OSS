#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using MMP.Herald.Events;

namespace MMP.Herald.Addons.Query;

/// <summary>
/// Walks a <see cref="QueryExpression"/> and returns whether a given
/// <see cref="LogEvent"/> satisfies it. Kept pure and allocation-light so
/// it can run in-line on the server's ring buffer for live search.
/// </summary>
public static class QueryEvaluator
{
    // Regex matches capped at 200ms — catastrophic backtracking is a DoS risk
    // when users can submit query strings.
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Returns <c>true</c> when <paramref name="evt"/> matches <paramref name="expr"/>.
    /// </summary>
    public static bool Evaluate(QueryExpression expr, LogEvent evt) => expr switch
    {
        AndExpression a => Evaluate(a.Left, evt) && Evaluate(a.Right, evt),
        OrExpression o => Evaluate(o.Left, evt) || Evaluate(o.Right, evt),
        NotExpression n => !Evaluate(n.Inner, evt),
        TermExpression t => EvaluateTerm(t, evt),
        _ => false,
    };

    private static bool EvaluateTerm(TermExpression term, LogEvent evt)
    {
        var actual = ResolveField(term.FieldPath, evt);
        return actual is not null && Compare(actual, term.Op, term.Value);
    }

    // Field resolution dispatches by the head of the dotted path. `property.X`
    // forwards the remainder of the path to LogEvent's property lookup so
    // nested names with dots still resolve.
    private static object? ResolveField(IReadOnlyList<string> path, LogEvent evt)
    {
        if (path.Count == 0) return null;

        var head = path[0].ToLowerInvariant();
        return head switch
        {
            "level" => evt.Level?.Key,
            "category" => evt.Category?.Value,
            "message" => evt.Message,
            "template" => evt.MessageTemplate,
            "property" when path.Count >= 2 => evt.GetProperty(string.Join(".", path.Skip(1)))?.Value,
            _ => null,
        };
    }

    private static bool Compare(object actual, QueryComparisonOperator op, QueryValue value) => op switch
    {
        QueryComparisonOperator.Match => MatchOp(actual, value),
        QueryComparisonOperator.Equals => EqualsOp(actual, value),
        QueryComparisonOperator.NotEquals => !EqualsOp(actual, value),
        QueryComparisonOperator.Regex => RegexOp(actual, value),
        QueryComparisonOperator.Greater => OrderingOp(actual, value) > 0,
        QueryComparisonOperator.GreaterOrEqual => OrderingOp(actual, value) >= 0,
        QueryComparisonOperator.Less => OrderingOp(actual, value) < 0,
        QueryComparisonOperator.LessOrEqual => OrderingOp(actual, value) <= 0,
        _ => false,
    };

    // Natural match: numeric equality when both sides look like numbers,
    // otherwise case-insensitive substring containment.
    private static bool MatchOp(object actual, QueryValue value)
    {
        if (TryBothAsDouble(actual, value, out var lhs, out var rhs))
            return lhs.Equals(rhs);

        var actualStr = actual.ToString() ?? string.Empty;
        return actualStr.IndexOf(value.Text, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool EqualsOp(object actual, QueryValue value)
    {
        if (TryBothAsDouble(actual, value, out var lhs, out var rhs))
            return lhs.Equals(rhs);

        return string.Equals(actual.ToString() ?? string.Empty, value.Text, StringComparison.OrdinalIgnoreCase);
    }

    private static bool RegexOp(object actual, QueryValue value)
    {
        try
        {
            return Regex.IsMatch(
                actual.ToString() ?? string.Empty,
                value.Text,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                RegexTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            // Invalid pattern — behave as no-match rather than crashing the query.
            return false;
        }
    }

    private static int OrderingOp(object actual, QueryValue value)
    {
        if (TryBothAsDouble(actual, value, out var lhs, out var rhs))
            return lhs.CompareTo(rhs);

        return string.Compare(
            actual.ToString() ?? string.Empty,
            value.Text,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryBothAsDouble(object actual, QueryValue value, out double lhs, out double rhs)
    {
        lhs = 0;
        rhs = 0;
        // A quoted string literal is treated as text even if it looks numeric,
        // so the user can force string compare with quoting.
        if (value.IsString) return false;

        return TryActualAsDouble(actual, out lhs)
               && double.TryParse(value.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out rhs);
    }

    private static bool TryActualAsDouble(object actual, out double d)
    {
        switch (actual)
        {
            case double dv: d = dv; return true;
            case float fv: d = fv; return true;
            case int iv: d = iv; return true;
            case long lv: d = lv; return true;
            case short sv: d = sv; return true;
            case decimal mv: d = (double)mv; return true;
        }
        return double.TryParse(
            actual.ToString(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out d);
    }
}
