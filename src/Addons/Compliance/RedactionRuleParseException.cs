#nullable enable

using System;

namespace MMP.Herald.Addons.Compliance;

/// <summary>
/// Raised when <see cref="RedactionRuleParser"/> cannot parse a rule string,
/// either because the rule head is malformed or because the scope predicate
/// is not a valid <see cref="Query.LogEventQuery"/> expression. The message
/// identifies which half failed and includes the parser's own diagnostic.
/// </summary>
public sealed class RedactionRuleParseException : Exception
{
    public RedactionRuleParseException(string message) : base(message) { }
    public RedactionRuleParseException(string message, Exception inner) : base(message, inner) { }
}
