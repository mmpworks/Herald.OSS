// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System.Collections.Generic;
using System.Text;

namespace MMP.Herald.Generators;

/// <summary>
/// Generator-private copy of the built-in policy enum. Kept in the generator
/// assembly so the netstandard2.0 build doesn't have to reference the runtime
/// <c>MMP.Herald.Templating.BuiltinPolicy</c> (which lives in the net8+
/// Herald.OSS assembly). Tests that need to drive both build-time and
/// runtime paths from one fixture map between the two via a small helper.
/// </summary>
internal enum BuiltinPolicyKind
{
    Pascal = 0,
    Snake = 1,
    Camel = 2,
    Custom = 3,
}

/// <summary>
/// Build-time property-name resolver shared by every Herald source generator
/// that bakes resolved names into emitted code. Mirrors the runtime
/// <c>PascalCasePolicy</c> / <c>CamelCasePolicy</c> / <c>SnakeCasePolicy</c>
/// algorithms one-for-one so the build-time output is byte-identical to what
/// the runtime cache would produce on a cold miss.
///
/// <para>
/// <b>Per-policy shape.</b> All three built-ins select the same way:
/// template token name first, then CAE, then <c>argN</c>. They only differ
/// in the casing transform applied to the selected source.
/// <list type="bullet">
///   <item><see cref="BuiltinPolicyKind.Pascal"/>: <c>ToPascalCase</c> —
///         uppercase first letter when it's currently lowercase; leave
///         already-Pascal, all-caps, and underscored inputs alone.</item>
///   <item><see cref="BuiltinPolicyKind.Snake"/>: <c>ToSnakeCase</c> —
///         insert underscores at camel/Pascal boundaries with acronym
///         coalescing, then lowercase the result.</item>
///   <item><see cref="BuiltinPolicyKind.Camel"/>: <c>ToCamelCase</c> —
///         lowercase first letter when it's currently uppercase; leave
///         already-lowercase-start and underscored inputs alone (mirror of
///         Pascal's restraint, inverted case test).</item>
///   <item><see cref="BuiltinPolicyKind.Custom"/>: build-time bake is unavailable.
///         Callers must dispatch through the runtime <c>ResolveAll</c> path.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>netstandard2.0-safe.</b> No BCL types beyond what netstandard2.0 ships;
/// runs inside the generator assembly without any net8+ polyfill.
/// </para>
/// </summary>
internal static class CompileTimeNameResolver
{
    /// <summary>
    /// Resolve baked property names for one (template, caller-arg-expressions, policy)
    /// tuple. Returns an array of length <paramref name="caeNames"/>.Count; never null.
    /// </summary>
    /// <param name="template">Raw message template, e.g. <c>"user {UserId} signed in"</c>.</param>
    /// <param name="caeNames">
    /// Caller-argument-expression names per slot. Pass the parameter names from a
    /// <c>[HeraldLog]</c> partial-method declaration; pass the C# expression text from
    /// each interceptor call site. Use empty strings, not nulls, for unknown slots.
    /// </param>
    /// <param name="policy">Built-in policy whose precedence + transform applies.</param>
    internal static string[] Resolve(
        string template,
        IReadOnlyList<string> caeNames,
        BuiltinPolicyKind policy)
    {
        var tokens = ExtractTemplateTokenNames(template);
        var result = new string[caeNames.Count];

        for (var i = 0; i < caeNames.Count; i++)
        {
            // Source selection is identical for every built-in: token name
            // first, then CAE, then argN. The casing transform below is
            // what distinguishes the three.
            var source = i < tokens.Count && !IsNullOrEmpty(tokens[i])
                ? tokens[i]
                : (!IsNullOrEmpty(caeNames[i])
                    ? caeNames[i]
                    : "arg" + (i + 1));

            // Casing transform.
            result[i] = policy switch
            {
                BuiltinPolicyKind.Pascal => ToPascalCase(source),
                BuiltinPolicyKind.Snake => ToSnakeCase(source),
                BuiltinPolicyKind.Camel => ToCamelCase(source),
                _ /* Custom */       => source, // build-time bake unavailable; caller handles
            };
        }

        return result;
    }

    // -- Template-token extraction --------------------------------------------
    //
    // Lightweight extractor that pulls names out of {Name} / {@Name} / {$Name}
    // / {Name:format} shapes. Skips text segments entirely. Bracket-pair
    // tracking is single-pass; we don't model the full template grammar
    // because the resolver only needs the token names.

    private static IReadOnlyList<string> ExtractTemplateTokenNames(string template)
    {
        var names = new List<string>();
        var i = 0;
        while (i < template.Length)
        {
            if (template[i] != '{')
            {
                i++;
                continue;
            }
            var close = template.IndexOf('}', i + 1);
            if (close < 0) break;

            var raw = template.Substring(i + 1, close - i - 1);
            if (raw.Length > 0 && (raw[0] == '@' || raw[0] == '$'))
            {
                raw = raw.Substring(1);
            }
            var fmtColon = raw.IndexOf(':');
            if (fmtColon >= 0) raw = raw.Substring(0, fmtColon);

            names.Add(raw);
            i = close + 1;
        }
        return names;
    }

    // -- Casing transforms ----------------------------------------------------
    //
    // ToPascalCase mirrors PascalCasePolicy.ToPascalCase exactly: leaves
    // already-upper-start tokens (UserId, IPAddress) alone, respects
    // deliberately-underscored tokens (user_id stays user_id), uppercases the
    // first letter when it's lowercase.
    //
    // ToCamelCase is the inverse: leaves already-lower-start tokens (userId,
    // url) alone, respects deliberately-underscored tokens (user_id stays
    // user_id), lowercases the first letter when it's uppercase. Mirror of
    // Pascal's restraint with the case test inverted.
    //
    // ToSnakeCase mirrors SnakeCasePolicy.ToSnakeCase one-for-one: inserts
    // separators at camel/Pascal boundaries with acronym coalescing
    // (HTTPClient -> http_client), then lowercases the whole result.
    // Idempotent on already-snake input.

    private static string ToPascalCase(string source)
    {
        if (string.IsNullOrEmpty(source)) return source;

        if (char.IsUpper(source[0]) || source.IndexOf('_') >= 0)
        {
            return source;
        }
        return char.ToUpperInvariant(source[0]) + source.Substring(1);
    }

    private static string ToCamelCase(string source)
    {
        if (string.IsNullOrEmpty(source)) return source;

        if (char.IsLower(source[0]) || source.IndexOf('_') >= 0)
        {
            return source;
        }
        return char.ToLowerInvariant(source[0]) + source.Substring(1);
    }

    private static string ToSnakeCase(string source)
    {
        if (string.IsNullOrEmpty(source)) return source;

        var sb = new StringBuilder(source.Length + 8);
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (i == 0) { sb.Append(c); continue; }
            var prev = source[i - 1];

            if (char.IsUpper(c))
            {
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
        return sb.ToString().ToLowerInvariant();
    }

    private static bool IsNullOrEmpty(string? s) => string.IsNullOrEmpty(s);
}
