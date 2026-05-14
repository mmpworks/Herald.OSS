#nullable enable

using System;
using MMP.Herald.Events;
using MMP.Herald.Filters;

namespace MMP.Herald.Addons.Query;

/// <summary>
/// <see cref="ILogFilter"/> adapter that evaluates a Herald query-DSL
/// expression per event. Turns a string predicate into something the
/// pipeline can gate on, analogous to Serilog's
/// <c>Filter.ByIncludingOnly("...")</c>.
/// </summary>
/// <remarks>
/// <para>
/// The expression syntax is the same one <see cref="LogEventQuery"/>
/// already parses for the search and archive-predicate surfaces — one
/// grammar across the whole codebase, one place to extend, one set of
/// tests to pin invariants. Supported operators: <c>=</c>, <c>!=</c>,
/// <c>~</c> (regex), <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c>,
/// <c>AND</c>, <c>OR</c>, <c>NOT</c>, parenthesised groups, and dotted
/// field paths.
/// </para>
/// <example>
/// <code>
/// // Only WARN-or-above events in the Combat category reach the sinks.
/// var filter = new ExpressionLogFilter("level:warn AND category:Combat");
/// builder.WithFilterExpression("level:warn AND category:Combat");
/// </code>
/// </example>
/// <para>
/// Parsing happens once at construction. The compiled <see cref="LogEventQuery"/>
/// instance is then invoked per event — the hot path is a tree walk on
/// the already-built AST, not a re-parse. Invalid expressions surface
/// as <see cref="QueryParseException"/> from the constructor so
/// misconfigurations fail at pipeline build, not at first log call.
/// </para>
/// <para>
/// <b>Drop attribution.</b> Rejections by this filter flow through the
/// same <see cref="MMP.Herald.Metrics.IPipelineDropSink"/> hook every
/// other filter uses. The canonical drop reason is
/// <c>DropReasons.Predicate</c>, matching the built-in
/// <see cref="MMP.Herald.Predicates.PredicateFilter"/> classification.
/// </para>
/// <para>
/// <b>Perf note.</b> The expression walks the AST on every call, which is
/// typically 20-80 ns depending on expression complexity. For hot-loop
/// game code that logs millions of events per second, prefer a compiled
/// <see cref="MMP.Herald.Predicates.CompiledPredicateFilter"/> authored
/// directly in C#. For the standard "let ops drop into a config file
/// and add a filter" scenario this addon is the right shape.
/// </para>
/// </remarks>
public sealed class ExpressionLogFilter : ILogFilter
{
    private readonly LogEventQuery _query;
    private readonly string _source;

    /// <summary>
    /// Parse <paramref name="expression"/> and construct a filter that
    /// admits events matching it.
    /// </summary>
    /// <param name="expression">Query DSL string (see class remarks for
    /// the operator set).</param>
    /// <exception cref="ArgumentException">Thrown when the expression is
    /// null, empty, or whitespace.</exception>
    /// <exception cref="QueryParseException">Thrown when the expression
    /// fails to parse. The message names the failing token or position.</exception>
    public ExpressionLogFilter(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        _source = expression;
        _query = LogEventQuery.Parse(expression);
    }

    /// <summary>
    /// The original expression string, preserved for diagnostics
    /// (management-API introspection, validation reports, test
    /// assertions).
    /// </summary>
    public string Expression => _source;

    /// <inheritdoc />
    public bool Allow(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        return _query.Matches(logEvent);
    }
}
