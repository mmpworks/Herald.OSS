#nullable enable

using System;
using Herald.OSS.Serilog.Expressions.Ast;
using Herald.OSS.Serilog.Expressions.Eval;
using Herald.OSS.Serilog.Expressions.Parsing;
using MMP.Herald.Events;
using MMP.Herald.Filters;

namespace Herald.OSS.Serilog.Expressions.Filtering;

/// <summary>
/// <see cref="ILogFilter"/> that admits or rejects an event based on a Serilog
/// expression. The expression string is parsed once at construction; the cached
/// AST is tree-walked per event.
///
/// <para>
/// Two modes mirror Serilog.Expressions: <c>ByIncludingOnly</c> admits events
/// for which the expression evaluates true; <c>ByExcluding</c> admits events
/// for which it does NOT. The "is the result true" coercion (the single point
/// where an undefined result becomes false) happens here at the boundary.
/// </para>
/// </summary>
public sealed class ExpressionFilter : ILogFilter
{
    private readonly ExpressionNode _ast;
    private readonly bool _admitWhenTrue;

    /// <summary>The original expression string, preserved for diagnostics.</summary>
    public string Expression { get; }

    private ExpressionFilter(string expression, bool admitWhenTrue)
    {
        Expression = expression;
        _admitWhenTrue = admitWhenTrue;
        // Parse-once-in-ctor: misconfiguration fails at pipeline build, not at
        // first log call. Same shape as the Query addon's ExpressionLogFilter.
        _ast = ExpressionParser.Parse(expression);
    }

    /// <summary>
    /// Build a filter that admits ONLY events matching <paramref name="expression"/>.
    /// Mirrors Serilog's <c>Filter.ByIncludingOnly</c>.
    /// </summary>
    /// <exception cref="ExpressionParseException">The expression failed to parse.</exception>
    public static ExpressionFilter IncludingOnly(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        return new ExpressionFilter(expression, admitWhenTrue: true);
    }

    /// <summary>
    /// Build a filter that admits all events EXCEPT those matching
    /// <paramref name="expression"/>. Mirrors Serilog's <c>Filter.ByExcluding</c>.
    /// </summary>
    /// <exception cref="ExpressionParseException">The expression failed to parse.</exception>
    public static ExpressionFilter Excluding(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        return new ExpressionFilter(expression, admitWhenTrue: false);
    }

    /// <inheritdoc />
    public bool Allow(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        // The only place undefined collapses to false: a non-true result means
        // the expression did not match. ByIncludingOnly admits a match;
        // ByExcluding admits a non-match.
        var matched = Coerce.ToFilterBool(ExpressionEvaluator.Evaluate(_ast, logEvent));
        return _admitWhenTrue ? matched : !matched;
    }
}
