// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using System.Text;

namespace MMP.Herald.Templating.NamingPolicies;

/// <summary>
/// Template-tokens-win naming policy that converts the chosen name to
/// <c>snake_case</c>. Aimed at OpenTelemetry attribute conventions and
/// Python-flavored downstream consumers (Loki / Promtail label keys live in a
/// different shape — see the deferred <c>KebabCasePolicy</c>).
///
/// <para>
/// Algorithm (per slot):
/// <list type="number">
///   <item>Pick the source: token name if present, else caller-argument-expression
///         name, else <c>"argN"</c>.</item>
///   <item>Walk the source. Insert an <c>_</c> at every camel/Pascal boundary
///         (a lowercase or digit immediately followed by an uppercase letter).</item>
///   <item>For runs of two-or-more uppercase letters (acronyms), coalesce them:
///         the run stays a single word, with an <c>_</c> only at the boundary
///         to the next word. <c>HTTPClient</c> → <c>http_client</c>,
///         not <c>h_t_t_p_client</c>.</item>
///   <item>Lowercase the whole result.</item>
///   <item>Collapse double underscores produced by adjacent already-underscored
///         and casing transitions (so <c>__user</c> stays <c>__user</c>, not
///         <c>_user</c> — leading underscores preserved).</item>
/// </list>
/// </para>
///
/// <para>
/// Idempotent: already-<c>snake_case</c> input round-trips unchanged.
/// </para>
///
/// <para>
/// Examples:
/// <list type="bullet">
///   <item><c>{UserId}</c> → <c>user_id</c>.</item>
///   <item><c>{IPAddress}</c> → <c>ip_address</c>.</item>
///   <item><c>{HTTPClient}</c> → <c>http_client</c>.</item>
///   <item><c>{HTTPSConnection}</c> → <c>https_connection</c>.</item>
///   <item><c>{userIDValue}</c> → <c>user_id_value</c>.</item>
///   <item><c>{URLPath}</c> → <c>url_path</c>.</item>
///   <item><c>{user_id}</c> → <c>user_id</c> (idempotent).</item>
///   <item><c>{__user}</c> → <c>__user</c> (leading underscores preserved).</item>
///   <item><c>{X}</c> → <c>x</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// Stateless and thread-safe.
/// </para>
/// </summary>
public sealed class SnakeCasePolicy : IPropertyNamingPolicy
{
    /// <summary>Singleton instance. Use this rather than constructing new instances.</summary>
    public static readonly SnakeCasePolicy Instance = new();

    /// <inheritdoc/>
    public string Id => "snake";

    /// <inheritdoc/>
    public string[] ResolveAll(
        ReadOnlySpan<MessageTemplateToken.Property> tokens,
        ReadOnlySpan<string> argExprs)
    {
        var result = new string[argExprs.Length];
        for (var i = 0; i < argExprs.Length; i++)
        {
            var source = i < tokens.Length && !string.IsNullOrEmpty(tokens[i].Name)
                ? tokens[i].Name
                : argExprs[i];

            result[i] = ToSnakeCase(string.IsNullOrEmpty(source) ? "arg" + (i + 1) : source);
        }
        return result;
    }

    /// <summary>
    /// Walks the input once, inserting underscores at camel/Pascal boundaries
    /// while coalescing acronym runs. See class doc for the full rule.
    /// </summary>
    private static string ToSnakeCase(string source)
    {
        if (source.Length == 0) return source;

        // Cognitive Complexity note: the loop has three logical states —
        // 1) we are at a casing transition that needs a separator,
        // 2) we are inside an uppercase run (an acronym),
        // 3) we are in a steady-state lowercase/digit/separator section.
        // Each iteration reads two characters of context (current + next) so
        // the acronym-vs-Pascal-boundary decision is local.
        var sb = new StringBuilder(source.Length + 8);

        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];

            if (i == 0)
            {
                // First character: emit as-is (lowercased at the end). Don't
                // insert a leading underscore even if the source starts upper,
                // since "UserId" should become "user_id", not "_user_id".
                sb.Append(c);
                continue;
            }

            var prev = source[i - 1];

            if (char.IsUpper(c))
            {
                // Boundary cases that need an underscore before this uppercase:
                //   a) prev is lowercase or digit            → "userId"     -> "user_id"
                //   b) prev is uppercase AND next is lower   → "HTTPClient" -> "http_client"
                //      (we are leaving an acronym run into a Pascal word)
                var prevIsLowerOrDigit = char.IsLower(prev) || char.IsDigit(prev);
                var nextExists = i + 1 < source.Length;
                var nextIsLower = nextExists && char.IsLower(source[i + 1]);
                var prevIsUpper = char.IsUpper(prev);

                if (prevIsLowerOrDigit || (prevIsUpper && nextIsLower))
                {
                    sb.Append('_');
                }
            }

            sb.Append(c);
        }

        // Lowercase the whole thing at once — cheaper than per-char, and
        // ToLowerInvariant on an already-lower string is a fast path.
        return sb.ToString().ToLowerInvariant();
    }
}
