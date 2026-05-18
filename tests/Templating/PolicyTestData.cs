#nullable enable

using MMP.Herald.Templating;

namespace MMP.Herald.OSS.Tests.Templating;

/// <summary>
/// Shared edge-case inputs every policy is asserted against. New rows added
/// here propagate to every policy test class automatically.
/// </summary>
/// <remarks>
/// All three built-ins are token-first; the per-policy columns differ only in
/// the casing transform applied to the selected source. Camel mirrors Pascal's
/// restraint (already-cased / underscored inputs pass through) with the case
/// test inverted — lowercase the first letter only when it's currently
/// uppercase.
/// </remarks>
internal static class PolicyTestData
{
    /// <summary>
    /// Synthesizes a single-token template + arg-expression pair so each row
    /// in <see cref="EdgeCases"/> can drive a one-arg <c>ResolveAll</c> call.
    /// </summary>
    internal static (MessageTemplateToken.Property[] tokens, string[] argExprs) Pair(string tokenName, string argExpr)
    {
        var tokens = new[]
        {
            new MessageTemplateToken.Property(
                Name: tokenName,
                CaptureMode: LogPropertyCaptureMode.Default,
                Format: null,
                RawText: "{" + tokenName + "}"),
        };
        var argExprs = new[] { argExpr };
        return (tokens, argExprs);
    }

    /// <summary>
    /// Token-name cases each policy is verified against. The <c>Arg</c> column
    /// is the CAE name at the call site; since the built-ins are token-first,
    /// the CAE only matters when the token slot is empty (covered by the
    /// dedicated "fall back" tests in each policy class).
    /// </summary>
    internal static readonly (string Token, string Arg, string Pascal, string Camel, string Snake)[] EdgeCases =
    {
        // (Token,           Arg,        Pascal,           Camel,             Snake)
        ("UserId",           "userId",   "UserId",         "userId",          "user_id"),
        ("userId",           "userId",   "UserId",         "userId",          "user_id"),
        ("HTTPClient",       "client",   "HTTPClient",     "hTTPClient",      "http_client"),
        ("IPAddress",        "ipAddr",   "IPAddress",      "iPAddress",       "ip_address"),
        ("userIDValue",      "v",        "UserIDValue",    "userIDValue",     "user_id_value"),
        ("URLPath",          "p",        "URLPath",        "uRLPath",         "url_path"),
        ("HTTPSConnection",  "c",        "HTTPSConnection","hTTPSConnection", "https_connection"),
        ("USERID",           "x",        "USERID",         "uSERID",          "userid"),
        ("X",                "x",        "X",              "x",               "x"),
        ("user_id",          "userId",   "user_id",        "user_id",         "user_id"),
        ("_user",            "userId",   "_user",          "_user",           "_user"),
        ("__user",           "userId",   "__user",         "__user",          "__user"),
    };
}
