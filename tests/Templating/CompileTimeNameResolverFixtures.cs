// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.OSS.Tests.Templating;

/// <summary>
/// Shared row-based fixtures for the build-time and runtime name-resolution
/// paths. Each <see cref="Row"/> is a single asserting case
/// <c>(template, caeNames, policyId, expected)</c>.
///
/// <para>
/// Two test classes consume these fixtures:
/// <list type="bullet">
///   <item><c>CompileTimeNameResolverTests</c> — runs every row through the
///         build-time <c>CompileTimeNameResolver.Resolve</c>.</item>
///   <item><c>PolicyResolveAllFixtureTests</c> — runs the same rows through
///         the runtime <c>PascalCasePolicy.ResolveAll</c> /
///         <c>SnakeCasePolicy.ResolveAll</c> / <c>CamelCasePolicy.ResolveAll</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why string policy ids and not the BuiltinPolicy enum directly.</b>
/// The fixtures are <c>public</c> so xUnit's MemberData discovery finds them
/// without any IVT gymnastics. The build-time and runtime sides each map the
/// string id to their own enum / policy instance internally — the fixtures
/// stay independent of either side's representation.
/// </para>
///
/// <para>
/// <b>Asymmetry note.</b> The empty-CAE fallback is reachable only on the
/// runtime path — the build-time path always has a non-empty parameter name
/// for <c>[HeraldLog]</c> and a non-empty source-code expression text for
/// interceptor call sites. Rows marked <see cref="Row.RuntimeOnly"/> exercise
/// that asymmetric axis; the build-time test class skips them.
/// </para>
/// </summary>
public static class CompileTimeNameResolverFixtures
{
    /// <summary>
    /// One assertion row.
    /// </summary>
    /// <param name="Description">Human-readable description (used as the xUnit data label).</param>
    /// <param name="Template">Raw template, e.g. <c>"user {UserId}"</c>.</param>
    /// <param name="CaeNames">CAE names per slot. Empty string = unknown.</param>
    /// <param name="PolicyId">
    /// Policy identifier (<c>"pascal"</c>, <c>"snake"</c>, or <c>"camel"</c>).
    /// String form keeps the fixture independent of the internal
    /// <c>BuiltinPolicy</c> enum.
    /// </param>
    /// <param name="Expected">Expected output array (length matches CaeNames).</param>
    /// <param name="RuntimeOnly">
    /// True when the row exercises the empty-CAE fallback that the build-time
    /// path cannot reach. The build-time test class filters these out.
    /// </param>
    public sealed record Row(
        string Description,
        string Template,
        string[] CaeNames,
        string PolicyId,
        string[] Expected,
        bool RuntimeOnly = false);

    /// <summary>
    /// Every row across every built-in policy. The xUnit data-driven tests
    /// flatten this into one test case per row.
    /// </summary>
    public static IReadOnlyList<Row> All { get; } = new[]
    {
        // -- Matched arity (token count == args) -----------------------------

        new Row(
            "Pascal: single PascalCase token preserved",
            "user {UserId} signed in",
            new[] { "userId" },
            "pascal",
            new[] { "UserId" }),

        new Row(
            "Snake: single PascalCase token to snake_case",
            "user {UserId} signed in",
            new[] { "userId" },
            "snake",
            new[] { "user_id" }),

        new Row(
            "Camel: single PascalCase token to camelCase",
            "user {UserId} signed in",
            new[] { "userId" },
            "camel",
            new[] { "userId" }),

        new Row(
            "Camel: token wins over differently-named CAE (token cased to camel)",
            "user {UserId} signed in",
            new[] { "accountId" },
            "camel",
            new[] { "userId" }),

        // -- Multi-arg matched ------------------------------------------------

        new Row(
            "Pascal: two tokens preserved + cased",
            "{UserId} bought {ItemCount}",
            new[] { "userId", "itemCount" },
            "pascal",
            new[] { "UserId", "ItemCount" }),

        new Row(
            "Snake: two tokens snake_cased",
            "{UserId} bought {ItemCount}",
            new[] { "userId", "itemCount" },
            "snake",
            new[] { "user_id", "item_count" }),

        new Row(
            "Camel: two tokens camelCased",
            "{UserId} bought {ItemCount}",
            new[] { "userId", "itemCount" },
            "camel",
            new[] { "userId", "itemCount" }),

        // -- Under-templated (fewer tokens than args) -------------------------
        // The first slot has a token (token wins, then cased); the second
        // slot has no token so it falls back to the CAE name (then cased).

        new Row(
            "Pascal: under-templated falls back to CAE for trailing slot",
            "{A} only",
            new[] { "first", "second" },
            "pascal",
            new[] { "A", "Second" }),

        new Row(
            "Snake: under-templated falls back to CAE for trailing slot",
            "{A} only",
            new[] { "first", "second" },
            "snake",
            new[] { "a", "second" }),

        new Row(
            "Camel: under-templated falls back to CAE for trailing slot",
            "{A} only",
            new[] { "first", "second" },
            "camel",
            new[] { "a", "second" }),

        // -- Over-templated (more tokens than args) ---------------------------
        // Tokens past the arg count are simply ignored — only the args count
        // shapes the output length.

        new Row(
            "Pascal: over-templated ignores extra tokens",
            "{A} {B} {C}",
            new[] { "first" },
            "pascal",
            new[] { "A" }),

        new Row(
            "Camel: over-templated ignores extra tokens",
            "{A} {B} {C}",
            new[] { "first" },
            "camel",
            new[] { "a" }),

        // -- Empty CAE in some slots (runtime-only path) ----------------------
        // [HeraldLog] always has a non-empty parameter name; interceptor call
        // sites always have a non-empty CAE expression text. The empty-CAE
        // fallback only fires on the runtime path when a caller explicitly
        // passes "" via the params overload.

        new Row(
            "Pascal: empty CAE falls back to token (runtime-only)",
            "user {UserId} signed in",
            new[] { "" },
            "pascal",
            new[] { "UserId" },
            RuntimeOnly: true),

        new Row(
            "Camel: empty CAE falls back to token + camelCases (runtime-only)",
            "user {UserId} signed in",
            new[] { "" },
            "camel",
            new[] { "userId" },
            RuntimeOnly: true),

        new Row(
            "Pascal: empty CAE + empty token falls back to argN (runtime-only)",
            "static text",
            new[] { "" },
            "pascal",
            new[] { "Arg1" },
            RuntimeOnly: true),

        new Row(
            "Camel: empty CAE + empty token falls back to argN (runtime-only)",
            "static text",
            new[] { "" },
            "camel",
            new[] { "arg1" },
            RuntimeOnly: true),

        // -- Dotted CAE expressions ------------------------------------------
        // Pascal/Snake/Camel all use the token first when one exists; the CAE
        // only matters at slots without a token. Consumers using dotted
        // property-access expressions at the call site live with the dotted
        // name when no token shadows the slot — that's the CAE contract.

        new Row(
            "Camel: token wins over dotted CAE (token cased)",
            "user {UserId} signed in",
            new[] { "currentUser.Id" },
            "camel",
            new[] { "userId" }),

        new Row(
            "Camel: dotted CAE falls back from missing token",
            "no tokens",
            new[] { "currentUser.Id" },
            "camel",
            new[] { "currentUser.Id" }),

        // -- Casing edge cases (Pascal) --------------------------------------

        new Row(
            "Pascal: already-uppercase preserved (UserId)",
            "{UserId}",
            new[] { "userId" },
            "pascal",
            new[] { "UserId" }),

        new Row(
            "Pascal: all-caps preserved (USERID)",
            "{USERID}",
            new[] { "userId" },
            "pascal",
            new[] { "USERID" }),

        new Row(
            "Pascal: lowercase-start gets first-letter cased (userId -> UserId)",
            "{userId}",
            new[] { "userId" },
            "pascal",
            new[] { "UserId" }),

        new Row(
            "Pascal: underscored preserved (user_id stays user_id)",
            "{user_id}",
            new[] { "userId" },
            "pascal",
            new[] { "user_id" }),

        // -- Casing edge cases (Camel) ---------------------------------------
        // Mirror of Pascal's restraint with the case test inverted.

        new Row(
            "Camel: already-lowercase preserved (userId)",
            "{userId}",
            new[] { "userId" },
            "camel",
            new[] { "userId" }),

        new Row(
            "Camel: uppercase-start gets first-letter cased (UserId -> userId)",
            "{UserId}",
            new[] { "userId" },
            "camel",
            new[] { "userId" }),

        new Row(
            "Camel: underscored preserved (user_id stays user_id)",
            "{user_id}",
            new[] { "userId" },
            "camel",
            new[] { "user_id" }),

        // -- Casing edge cases (Snake) ---------------------------------------

        new Row(
            "Snake: idempotent on already-snake input",
            "{user_id}",
            new[] { "userId" },
            "snake",
            new[] { "user_id" }),

        new Row(
            "Snake: acronym coalesced (HTTPClient -> http_client)",
            "{HTTPClient}",
            new[] { "client" },
            "snake",
            new[] { "http_client" }),

        new Row(
            "Snake: long acronym (HTTPSConnection -> https_connection)",
            "{HTTPSConnection}",
            new[] { "c" },
            "snake",
            new[] { "https_connection" }),

        new Row(
            "Snake: mixed (userIDValue -> user_id_value)",
            "{userIDValue}",
            new[] { "v" },
            "snake",
            new[] { "user_id_value" }),

        new Row(
            "Snake: single char (X -> x)",
            "{X}",
            new[] { "x" },
            "snake",
            new[] { "x" }),

        // -- Special chars in template tokens --------------------------------
        // {@Name} (destructuring) and {$Name} (stringification) strip their
        // sigil. {Name:format} strips the format spec.

        new Row(
            "Pascal: destructuring sigil @ stripped",
            "{@Order}",
            new[] { "order" },
            "pascal",
            new[] { "Order" }),

        new Row(
            "Pascal: stringification sigil $ stripped",
            "{$Order}",
            new[] { "order" },
            "pascal",
            new[] { "Order" }),

        new Row(
            "Pascal: format spec stripped (UserId:N0)",
            "{UserId:N0}",
            new[] { "userId" },
            "pascal",
            new[] { "UserId" }),

        new Row(
            "Snake: format spec stripped (UserId:N0 -> user_id)",
            "{UserId:N0}",
            new[] { "userId" },
            "snake",
            new[] { "user_id" }),

        // -- Long templates --------------------------------------------------

        new Row(
            "Pascal: long template with 4 tokens",
            "{First} bought {Second} for {Third} from {Fourth}",
            new[] { "a", "b", "c", "d" },
            "pascal",
            new[] { "First", "Second", "Third", "Fourth" }),

        new Row(
            "Snake: long template with 4 tokens",
            "{FirstName} {LastName} ({Age}) from {HomeCity}",
            new[] { "f", "l", "a", "h" },
            "snake",
            new[] { "first_name", "last_name", "age", "home_city" }),

        new Row(
            "Camel: long template with 4 tokens",
            "{FirstName} {LastName} ({Age}) from {HomeCity}",
            new[] { "f", "l", "a", "h" },
            "camel",
            new[] { "firstName", "lastName", "age", "homeCity" }),
    };
}
