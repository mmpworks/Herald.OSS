#nullable enable

using MMP.Herald.Filters;

namespace Herald.OSS.Serilog.Expressions.Filtering;

/// <summary>
/// Serilog.Expressions-compatible filter factory. Mirrors the
/// <c>Serilog.Filter</c> API surface so existing Serilog code that calls
/// <c>Filter.ByExcluding("...")</c> / <c>Filter.ByIncludingOnly("...")</c>
/// recompiles against Herald with only a namespace change.
///
/// <para>
/// Each method compiles the expression string once and returns an
/// <see cref="ILogFilter"/> the pipeline gates on. An invalid expression throws
/// <see cref="Parsing.ExpressionParseException"/> here, at config time.
/// </para>
///
/// <example>
/// <code>
/// var noHealthChecks = Filter.ByExcluding("RequestPath like '/health%'");
/// var errorsOnly     = Filter.ByIncludingOnly("@Level >= 'Warning'");
/// </code>
/// </example>
/// </summary>
public static class Filter
{
    /// <summary>
    /// A filter that admits all events EXCEPT those matching
    /// <paramref name="expression"/>. Serilog parity:
    /// <c>Filter.ByExcluding(string)</c>.
    /// </summary>
    public static ILogFilter ByExcluding(string expression) =>
        ExpressionFilter.Excluding(expression);

    /// <summary>
    /// A filter that admits ONLY events matching <paramref name="expression"/>.
    /// Serilog parity: <c>Filter.ByIncludingOnly(string)</c>.
    /// </summary>
    public static ILogFilter ByIncludingOnly(string expression) =>
        ExpressionFilter.IncludingOnly(expression);
}
