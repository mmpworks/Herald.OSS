// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;

namespace MMP.Herald.Templating.NamingPolicies;

/// <summary>
/// Token-first camelCase naming policy. Property names come from the template
/// token when present, falling back to the caller-argument-expression name,
/// finally to <c>"argN"</c>. Then <c>ToCamelCase</c> lowercases the first
/// letter on names that don't already start lowercase and don't contain
/// underscores — the inverse of <see cref="PascalCasePolicy"/>'s casing rule,
/// preserving the same restraint on already-correct shapes and underscore-
/// bearing names.
///
/// <para>
/// Algorithm (per slot):
/// <list type="number">
///   <item>If a token exists at this slot, take its <see cref="MessageTemplateToken.Property.Name"/>.
///         Otherwise fall back to the caller-argument-expression name at this slot.</item>
///   <item>If the source is empty, return <c>"argN"</c> where N is the slot index (rare).</item>
///   <item>If the first char is already lowercase, return the source unchanged.</item>
///   <item>If the source contains an underscore, return the source unchanged
///         (preserves <c>snake_case</c> tokens a consumer wrote on purpose).</item>
///   <item>Otherwise, lowercase the first char only; preserve interior casing.</item>
/// </list>
/// </para>
///
/// <para>
/// Examples:
/// <list type="bullet">
///   <item><c>{UserId}</c> → property <c>userId</c>.</item>
///   <item><c>{userId}</c> → property <c>userId</c> (already lowercase-start).</item>
///   <item><c>{user_id}</c> → property <c>user_id</c> (underscore preserved).</item>
///   <item>no template token + variable <c>currentUser</c> → <c>currentUser</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// Stateless and thread-safe. The runtime memoizes the resolved name array per
/// <c>(GetType(), template)</c> pair in a process-static frozen cache.
/// </para>
/// </summary>
public sealed class CamelCasePolicy : IPropertyNamingPolicy
{
    /// <summary>Singleton instance. Use this rather than constructing new instances.</summary>
    public static readonly CamelCasePolicy Instance = new();

    /// <inheritdoc/>
    public string Id => "camel";

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

            result[i] = ToCamelCase(string.IsNullOrEmpty(source) ? "arg" + (i + 1) : source);
        }
        return result;
    }

    /// <summary>
    /// First-letter-only lowercase. Leaves already-camel and underscored
    /// inputs alone. See class doc for the full rule.
    /// </summary>
    private static string ToCamelCase(string source)
    {
        // Hands off: already starts lowercase, or contains an underscore the
        // caller wrote on purpose. The underscore check is what makes
        // {user_id} survive — Camel is opinionated about case, not about
        // separator style. Mirror of PascalCasePolicy.ToPascalCase with the
        // case test inverted.
        if (char.IsLower(source[0]) || source.Contains('_'))
        {
            return source;
        }

        // Build first-letter-down + tail-unchanged without allocating twice.
        return string.Create(source.Length, source, static (span, src) =>
        {
            span[0] = char.ToLowerInvariant(src[0]);
            src.AsSpan(1).CopyTo(span[1..]);
        });
    }
}
