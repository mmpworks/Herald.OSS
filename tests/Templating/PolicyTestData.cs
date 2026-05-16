#nullable enable

using MMP.Herald.Templating;

namespace MMP.Herald.OSS.Tests.Templating;

/// <summary>
/// Shared edge-case inputs every policy is asserted against. Captured from
/// the ratified spec's test matrix so all three policies are reviewed against
/// the same surface. New rows added here propagate to every policy test class
/// automatically.
/// </summary>
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
    /// Token-name cases each policy is verified against. The <c>arg</c> column
    /// keeps the call-site variable shape realistic (consumers pass locals,
    /// not literals).
    /// </summary>
    internal static readonly (string Token, string Arg, string Pascal, string Camel, string Snake)[] EdgeCases =
    {
        // (Token,           Arg,        Pascal,           Camel,      Snake)
        ("UserId",           "userId",   "UserId",         "userId",   "user_id"),
        ("userId",           "userId",   "UserId",         "userId",   "user_id"),
        ("HTTPClient",       "client",   "HTTPClient",     "client",   "http_client"),
        ("IPAddress",        "ipAddr",   "IPAddress",      "ipAddr",   "ip_address"),
        ("userIDValue",      "v",        "UserIDValue",    "v",        "user_id_value"),
        ("URLPath",          "p",        "URLPath",        "p",        "url_path"),
        ("HTTPSConnection",  "c",        "HTTPSConnection","c",        "https_connection"),
        ("USERID",           "x",        "USERID",         "x",        "userid"),
        ("X",                "x",        "X",              "x",        "x"),
        ("user_id",          "userId",   "user_id",        "userId",   "user_id"),
        ("_user",            "userId",   "_user",          "userId",   "_user"),
        ("__user",           "userId",   "__user",         "userId",   "__user"),
    };
}
