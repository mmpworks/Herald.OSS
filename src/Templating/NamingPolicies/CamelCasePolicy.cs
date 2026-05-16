// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;

namespace MMP.Herald.Templating.NamingPolicies;

/// <summary>
/// Caller-argument-expression-wins naming policy. Preserves the pre-1.0 Herald
/// behavior for consumers who built around it and don't want to migrate their
/// downstream property schema.
///
/// <para>
/// Algorithm (per slot): use the caller-argument-expression name verbatim
/// (the C# variable name at the call site, captured via
/// <c>[CallerArgumentExpression]</c>). When that's empty, fall back to the
/// template token name; when both are empty, <c>"argN"</c>.
/// </para>
///
/// <para>
/// The template token <c>{UserId}</c> drives only the message rendering; the
/// emitted property name comes from the call site. This is the exact opposite
/// of <c>PascalCasePolicy</c>, which prefers the token.
/// </para>
///
/// <para>
/// Examples:
/// <list type="bullet">
///   <item><c>{UserId}</c> + <c>userId</c> → property <c>userId</c>.</item>
///   <item><c>{UserId}</c> + <c>accountId</c> → property <c>accountId</c>.</item>
///   <item><c>"static template"</c> + <c>userId</c> → property <c>userId</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// Stateless and thread-safe. The runtime memoizes the resolved name array.
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
            // Caller expression wins; fall back to token name; finally to argN.
            var name = !string.IsNullOrEmpty(argExprs[i])
                ? argExprs[i]
                : i < tokens.Length && !string.IsNullOrEmpty(tokens[i].Name)
                    ? tokens[i].Name
                    : "arg" + (i + 1);

            result[i] = name;
        }
        return result;
    }
}
