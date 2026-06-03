#nullable enable

using System;

namespace Herald.OSS.Serilog.Expressions.Parsing;

/// <summary>
/// Thrown when an expression string fails to tokenize or parse — including an
/// unknown built-in function or accessor name. Raised from the parser (and so
/// from <c>Filter.ByExcluding</c> / <c>ByIncludingOnly</c> at config time),
/// never per event. Mirrors the Query DSL's <c>QueryParseException</c> shape so
/// callers handle both surfaces the same way.
/// </summary>
public sealed class ExpressionParseException : Exception
{
    public ExpressionParseException(string message) : base(message) { }

    public ExpressionParseException(string message, Exception innerException)
        : base(message, innerException) { }
}
