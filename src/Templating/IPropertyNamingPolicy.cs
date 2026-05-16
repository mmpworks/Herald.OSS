// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;

namespace MMP.Herald.Templating;

/// <summary>
/// Resolves the property names emitted by Herald's typed-args call shapes
/// (e.g. <c>logger.Info("user {UserId} signed in", userId)</c>).
///
/// <para>
/// Three built-in policies ship in <see cref="NamingPolicies"/>:
/// <list type="bullet">
///   <item><c>PascalCasePolicy</c> — the default. Template tokens win
///       (matches Serilog / NLog / Microsoft.Extensions.Logging convention).</item>
///   <item><c>CamelCasePolicy</c> — the caller-argument-expression name
///       wins (preserves the pre-1.0 Herald behavior).</item>
///   <item><c>SnakeCasePolicy</c> — template tokens win, converted to
///       <c>snake_case</c> for OpenTelemetry / Python-flavored downstreams.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Contract for implementers.</b>
/// <list type="number">
///   <item>The returned array MUST have length equal to <paramref name="argExprs"/>'s length.
///       Mismatched length is a contract violation; the runtime will throw
///       <c>PolicyContractException</c> on validation.</item>
///   <item>All returned entries MUST be non-null and non-empty.</item>
///   <item>The returned array is treated as <b>read-only by convention</b> by the
///       Herald runtime. Once handed back, do not mutate it — the runtime copies
///       it into a cache-owned frozen array, but a policy that mutates the array
///       between return and copy invites a race.</item>
///   <item>The method MUST be pure and side-effect-free. The Herald cache assumes
///       <c>ResolveAll(tokens, argExprs)</c> returns the same names every time
///       for the same inputs.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Cold path vs hot path.</b> <c>ResolveAll</c> is invoked at most once per
/// <c>(policy.GetType(), template)</c> pair across the process lifetime. The
/// resolved array is cached in a process-static frozen cache and re-served by
/// reference on subsequent hits. Implementers do not need to memoize internally.
/// </para>
/// </summary>
public interface IPropertyNamingPolicy
{
    /// <summary>
    /// Resolves the property name for every typed-args slot in a single template.
    /// </summary>
    /// <param name="tokens">
    /// The named property tokens parsed from the template (e.g. <c>{UserId}</c>,
    /// <c>{Stage:N0}</c>, <c>{@User}</c>). Span may be shorter or longer than
    /// <paramref name="argExprs"/>; implementers decide the fallback shape per
    /// position.
    /// </param>
    /// <param name="argExprs">
    /// The <c>[CallerArgumentExpression]</c>-captured source text for each
    /// argument at the call site (e.g. <c>"userId"</c> for a local named
    /// <c>userId</c>). Always present at length equal to the typed-args arity.
    /// Implementers should fall back to these when a token is missing.
    /// </param>
    /// <returns>
    /// A <see cref="string"/> array of length <c>argExprs.Length</c>, with one
    /// resolved name per slot. Entries must be non-null and non-empty.
    /// The returned array is treated as read-only by the runtime — see contract
    /// in the type doc.
    /// </returns>
    string[] ResolveAll(
        ReadOnlySpan<MessageTemplateToken.Property> tokens,
        ReadOnlySpan<string> argExprs);

    /// <summary>
    /// Stable identifier for this policy. Used as the value in JSON config
    /// (<c>"namingPolicy": "pascal"</c>) and for diagnostics. Built-in IDs are
    /// <c>"pascal"</c>, <c>"camel"</c>, and <c>"snake"</c>.
    ///
    /// <para>
    /// <b>NOT used as the cache discriminator.</b> The Herald cache keys on
    /// <c>policy.GetType()</c>; <see cref="Id"/> is for round-trip serialization
    /// and operator-visible diagnostics only. Two policies returning the same
    /// <see cref="Id"/> are a registration error and the
    /// <c>NamingPolicyRegistry</c> rejects the second one unless the
    /// <see cref="Type"/> matches.
    /// </para>
    /// </summary>
    string Id { get; }
}
