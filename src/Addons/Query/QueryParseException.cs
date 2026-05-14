#nullable enable

using System;

namespace MMP.Herald.Addons.Query;

/// <summary>
/// Thrown when a query string fails tokenization or parsing. The message
/// carries the Superpower-provided position and expectation so operators
/// can fix the query without a debugger.
/// </summary>
public sealed class QueryParseException : Exception
{
    public QueryParseException(string message) : base(message) { }
    public QueryParseException(string message, Exception inner) : base(message, inner) { }
}
