// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Addons.ManagementApi;

/// <summary>
/// Named category of an authorization denial. A sealed record with
/// named static instances — same shape as
/// <see cref="MMP.Herald.Diagnostics.NoticeSeverity"/>: small surface,
/// closed-ish but additive. A future tier (e.g. <c>QuotaExceeded</c>)
/// is one new <see langword="static readonly"/> instance, not a
/// contract negotiation.
///
/// <para>
/// Subscribers (Dashboard, audit log, SIEM) can switch exhaustively
/// against the built-in names at compile time and stay open for new
/// kinds without breaking. The string <see cref="Name"/> is the wire
/// shape; structured renderers compare against the static instances.
/// </para>
/// </summary>
public sealed record DenialKind(string Name)
{
    /// <summary>
    /// The caller didn't authenticate, or authenticated as a principal
    /// the policy doesn't accept. The typical default rejection kind.
    /// </summary>
    public static readonly DenialKind Auth = new("auth");

    /// <summary>
    /// The license gate rejected the operation. Carries the structured
    /// license context (customer, product, expected accept-set,
    /// expiry) in <see cref="AuthorizationDecision.Facts"/> so the
    /// Dashboard renders a real license-failure UX instead of parsing
    /// a string.
    /// </summary>
    public static readonly DenialKind License = new("license");

    /// <summary>
    /// A higher-level policy denied the operation — quota, tenant
    /// scope, edition gate, business rule. Distinct from
    /// <see cref="Auth"/> (who you are) and <see cref="License"/>
    /// (what you bought).
    /// </summary>
    public static readonly DenialKind Policy = new("policy");

    public override string ToString() => Name;
}

/// <summary>
/// One typed value carrying the outcome of an
/// <see cref="IManagementApiAuthorizer"/> decision. Replaces the
/// earlier <c>(bool, out string?)</c> shape so the denial context
/// flows unchanged through the authorizer, the
/// <see cref="HeraldManagementApi.OnAuthorizationDenied"/> event,
/// <see cref="ManagementResult.Fail"/>, and the Dashboard renderer.
///
/// <para>
/// <b>Why a record struct.</b> The decision is small (a flag, a
/// nullable string reference, a nullable record reference, a
/// nullable dictionary reference) and frequently allocated on the
/// authorize-then-dispatch hot path. A readonly record struct avoids
/// the per-call heap allocation a class would impose while keeping
/// value-equality semantics free.
/// </para>
///
/// <para>
/// <b>Construction.</b> Use <see cref="Allow"/> for the happy path,
/// <see cref="Deny(string, DenialKind?, IReadOnlyDictionary{string,string}?)"/>
/// for rejections. Direct construction works too, but the factory
/// methods read better at the call site.
/// </para>
/// </summary>
/// <param name="Allowed">Whether the operation may proceed.</param>
/// <param name="Reason">Operator-readable rejection message. Null on success.</param>
/// <param name="Kind">Named category of the denial. Null on success.</param>
/// <param name="Facts">
/// Optional structured-context bag — customer id, expiry, product,
/// expected accept-set. Lets a license-failure UX render real fields
/// without a string-parse hack. Null when the authorizer has nothing
/// to add.
/// </param>
public readonly record struct AuthorizationDecision(
    bool Allowed,
    string? Reason,
    DenialKind? Kind,
    IReadOnlyDictionary<string, string>? Facts)
{
    /// <summary>
    /// Allow the operation. Reason, kind, and facts are all null.
    /// </summary>
    public static AuthorizationDecision Allow() => new(true, null, null, null);

    /// <summary>
    /// Deny the operation with an operator-readable
    /// <paramref name="reason"/>. <paramref name="kind"/> defaults to
    /// <see cref="DenialKind.Auth"/> — the most common rejection kind.
    /// Pass <paramref name="facts"/> when the denial has structured
    /// context worth carrying through to the renderer.
    /// </summary>
    public static AuthorizationDecision Deny(
        string reason,
        DenialKind? kind = null,
        IReadOnlyDictionary<string, string>? facts = null) =>
        new(false, reason, kind ?? DenialKind.Auth, facts);
}
