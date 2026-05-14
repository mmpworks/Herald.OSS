#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Events;

namespace MMP.Herald.Addons.Query;

/// <summary>
/// Public entry point for the Herald query DSL. Parses once, then evaluates
/// many — so searching a live ring buffer pays the parse cost a single time.
///
/// <example>
/// <code>
/// var query = LogEventQuery.Parse("level:error AND category:Combat");
/// var hits  = events.Where(query.Matches).ToList();
/// </code>
/// </example>
/// </summary>
public sealed class LogEventQuery
{
    private readonly QueryExpression _expression;

    private LogEventQuery(QueryExpression expression)
    {
        _expression = expression;
    }

    /// <summary>
    /// Parse a DSL string into a compiled <see cref="LogEventQuery"/>.
    /// Throws <see cref="QueryParseException"/> on syntax failure.
    /// </summary>
    public static LogEventQuery Parse(string input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        return new LogEventQuery(QueryParser.Parse(input));
    }

    /// <summary>
    /// Attempt to parse without throwing. Returns <c>true</c> on success with
    /// the compiled query in <paramref name="query"/>; otherwise <c>false</c>
    /// and the parse error in <paramref name="error"/>.
    /// </summary>
    public static bool TryParse(string input, out LogEventQuery? query, out string? error)
    {
        try
        {
            query = Parse(input);
            error = null;
            return true;
        }
        catch (QueryParseException ex)
        {
            query = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Evaluate this query against a single <see cref="LogEvent"/>.
    /// </summary>
    public bool Matches(LogEvent evt) => QueryEvaluator.Evaluate(_expression, evt);

    /// <summary>
    /// Enumerate the subset of <paramref name="events"/> satisfying this query.
    /// </summary>
    public IEnumerable<LogEvent> Filter(IEnumerable<LogEvent> events)
    {
        if (events is null) throw new ArgumentNullException(nameof(events));
        foreach (var evt in events)
        {
            if (Matches(evt)) yield return evt;
        }
    }

    /// <summary>
    /// The parsed AST, exposed for diagnostics and round-trip inspection.
    /// </summary>
    public QueryExpression Expression => _expression;
}
