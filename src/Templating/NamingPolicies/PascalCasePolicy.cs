// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;

namespace MMP.Herald.Templating.NamingPolicies;

/// <summary>
/// Default Herald.OSS 1.0+ property-naming policy. Template tokens win;
/// the first letter is uppercased only when it is currently lowercase.
/// Already-Pascal, all-caps, and underscored tokens are left as-is so a
/// deliberately-shaped token survives untouched.
///
/// <para>
/// Algorithm (per slot):
/// <list type="number">
///   <item>If a token exists at this slot, take its <see cref="MessageTemplateToken.Property.Name"/>.
///         Otherwise fall back to the caller-argument-expression name at this slot.</item>
///   <item>If the source is empty, return <c>"argN"</c> where N is the slot index (rare).</item>
///   <item>If the first char is already uppercase, return the source unchanged.</item>
///   <item>If the source contains an underscore, return the source unchanged
///         (preserves <c>snake_case</c> tokens a consumer wrote on purpose).</item>
///   <item>Otherwise, uppercase the first char only; preserve interior casing.</item>
/// </list>
/// </para>
///
/// <para>
/// Examples:
/// <list type="bullet">
///   <item><c>{UserId}</c> + <c>userId</c> → <c>UserId</c> (token unchanged).</item>
///   <item><c>{userId}</c> + <c>userId</c> → <c>UserId</c> (first letter cased).</item>
///   <item><c>{USERID}</c> + <c>userId</c> → <c>USERID</c> (already starts upper).</item>
///   <item><c>{user_id}</c> + <c>userId</c> → <c>user_id</c> (underscored, hands off).</item>
///   <item><c>{ipAddress}</c> + <c>ip</c> → <c>IpAddress</c> (acronym smartness deferred to v2).</item>
/// </list>
/// </para>
///
/// <para>
/// Stateless and thread-safe. The runtime memoizes the resolved name array per
/// <c>(GetType(), template)</c> pair in a process-static frozen cache.
/// </para>
/// </summary>
public sealed class PascalCasePolicy : IPropertyNamingPolicy
{
    /// <summary>Singleton instance. Use this rather than constructing new instances.</summary>
    public static readonly PascalCasePolicy Instance = new();

    /// <inheritdoc/>
    public string Id => "pascal";

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

            result[i] = ToPascalCase(string.IsNullOrEmpty(source) ? "arg" + (i + 1) : source);
        }
        return result;
    }

    /// <summary>
    /// First-letter-only uppercase. Leaves already-Pascal, all-caps, and
    /// underscored inputs alone. See class doc for the full rule.
    /// </summary>
    private static string ToPascalCase(string source)
    {
        // Hands off: already starts uppercase, or contains an underscore the
        // caller wrote on purpose. The underscore check is what makes
        // {user_id} survive — Pascal is opinionated about case, not about
        // separator style.
        if (char.IsUpper(source[0]) || source.Contains('_'))
        {
            return source;
        }

        // Build first-letter-up + tail-unchanged without allocating twice.
        return string.Create(source.Length, source, static (span, src) =>
        {
            span[0] = char.ToUpperInvariant(src[0]);
            src.AsSpan(1).CopyTo(span[1..]);
        });
    }
}
