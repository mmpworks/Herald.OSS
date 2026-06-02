#nullable enable

using System;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Configuration;

namespace Herald.OSS.Serilog.Expressions.Filtering;

/// <summary>
/// String-DSL filter overloads for the compat <see cref="LoggerFilterConfiguration"/>.
/// These live in the Apache-2.0 expressions package so the core compat assembly
/// (<c>MMP.Herald.Serilog</c>) carries no expression-engine dependency — the
/// dependency direction is Expressions -&gt; Serilog, never the reverse.
///
/// <para>
/// With <c>using Herald.OSS.Serilog.Expressions.Filtering;</c> in scope, a migrated
/// <c>.Filter.ByExcluding("RequestPath like '/health%'")</c> call site resolves these
/// extensions and gates the live pipeline — the same fluent shape Serilog.Expressions
/// hangs off <c>LoggerConfiguration.Filter</c>.
/// </para>
/// </summary>
public static class LoggerFilterConfigurationExpressionsExtensions
{
    /// <summary>
    /// Admit all events EXCEPT those matching the Serilog expression
    /// <paramref name="expression"/>. String-DSL form of Serilog's
    /// <c>Filter.ByExcluding(string)</c>. The expression is compiled once here; an
    /// invalid expression throws at config time.
    /// </summary>
    public static LoggerConfiguration ByExcluding(
        this LoggerFilterConfiguration filter, string expression)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return filter.With(ExpressionFilter.Excluding(expression));
    }

    /// <summary>
    /// Admit ONLY events matching the Serilog expression
    /// <paramref name="expression"/>. String-DSL form of Serilog's
    /// <c>Filter.ByIncludingOnly(string)</c>.
    /// </summary>
    public static LoggerConfiguration ByIncludingOnly(
        this LoggerFilterConfiguration filter, string expression)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return filter.With(ExpressionFilter.IncludingOnly(expression));
    }
}
