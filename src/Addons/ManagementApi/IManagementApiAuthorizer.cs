// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace MMP.Herald.Addons.ManagementApi;

/// <summary>
/// Authorization seam invoked at the head of every mutating method
/// on <see cref="HeraldManagementApi"/>. The Management API is the
/// single surface every HTTP, CLI, and game-console connector binds
/// to; an unauthenticated mutation reaching this surface mutates the
/// running log pipeline. The authorizer is the layer that decides
/// whether the caller is allowed to do that.
///
/// <para>
/// <b>Default is reject-all.</b> The OSS build ships
/// <see cref="RejectAllAuthorizer"/> as the default so a host that
/// hasn't wired authentication doesn't silently expose mutation. The
/// rejection message points the operator at the seam they need to
/// configure before going to production.
/// </para>
///
/// <para>
/// <b>Hybrid sync + async shape.</b> Mirror of the
/// <see cref="MMP.Herald.Pipeline.Kernel.IKernelSink"/> shape that
/// ships <c>Log</c> + <c>LogAsync</c> with a sync-forward default.
/// Sync authorizers (the common case — policy check against
/// in-memory state) implement <see cref="Decide"/> and inherit the
/// async wrap for free. Async authorizers (OPA, license-server
/// check-in in non-offline mode, ASP.NET Core
/// <c>IAuthorizationHandler</c> interop) override
/// <see cref="DecideAsync"/> with real async I/O. Either shape works
/// without forcing sync-over-async at the decorator.
/// </para>
///
/// <para>
/// <b>Returns a structured <see cref="AuthorizationDecision"/></b>
/// rather than a <c>(bool, out string?)</c> pair so the rejection
/// context (a typed <see cref="DenialKind"/>, optional structured
/// <c>Facts</c> like customerId / expiry / product) flows unchanged
/// from the authorizer through
/// <see cref="HeraldManagementApi.OnAuthorizationDenied"/>,
/// <see cref="ManagementResult.Fail(string, DenialKind?)"/>, and the
/// Dashboard renderer.
/// </para>
/// </summary>
public interface IManagementApiAuthorizer
{
    /// <summary>
    /// Synchronously decide whether the current caller is allowed to
    /// perform <paramref name="operation"/>. Implementations should
    /// be fast and side-effect-free — the authorizer is invoked on
    /// every mutating call.
    /// </summary>
    /// <param name="operation">
    /// The mutating-method name (e.g. <c>SetMinimumLevel</c>,
    /// <c>CommitFull</c>). Implementations can use this for audit
    /// logging and per-operation policy decisions.
    /// </param>
    /// <returns>
    /// An <see cref="AuthorizationDecision"/> describing the
    /// outcome. Use <see cref="AuthorizationDecision.Allow"/> for the
    /// happy path and
    /// <see cref="AuthorizationDecision.Deny(string, DenialKind?, System.Collections.Generic.IReadOnlyDictionary{string, string}?)"/>
    /// for rejections.
    /// </returns>
    AuthorizationDecision Decide(string operation);

    /// <summary>
    /// Async kernel-path entry. Default body forwards to
    /// <see cref="Decide"/> and wraps the result in a completed
    /// <see cref="ValueTask{TResult}"/>. Sync authorizers inherit
    /// this default and never pay the async-state-machine cost;
    /// async authorizers override with real async I/O.
    /// </summary>
    /// <param name="operation">
    /// The mutating-method name. Same contract as
    /// <see cref="Decide"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// Propagated to async I/O an override may perform. Sync
    /// authorizers can ignore it.
    /// </param>
    ValueTask<AuthorizationDecision> DecideAsync(
        string operation,
        CancellationToken cancellationToken = default) =>
        new(Decide(operation));
}

/// <summary>
/// Default OSS authorizer: rejects every mutating call. Keeps an
/// unconfigured host safe by default — a deployment that forgets to
/// wire an <see cref="IManagementApiAuthorizer"/> can't be tricked
/// into mutating its pipeline from any caller that holds a reference
/// to the API.
///
/// <para>
/// <b>Migration.</b> Replace with a real authorizer at construction
/// time:
/// <code>
/// var api = new HeraldManagementApi(builder, result, authorizer: new MyJwtAuthorizer(...));
/// </code>
/// or expose <see cref="AllowAllAuthorizer"/> deliberately in a
/// fully-trusted single-process test harness.
/// </para>
/// </summary>
public sealed class RejectAllAuthorizer : IManagementApiAuthorizer
{
    /// <summary>Shared instance — the authorizer holds no state.</summary>
    public static readonly RejectAllAuthorizer Instance = new();

    private const string RejectionReason =
        "HeraldManagementApi is unconfigured: no IManagementApiAuthorizer was supplied. " +
        "Wire one before exposing this API over HTTP, or pass AllowAllAuthorizer for a " +
        "deliberately-unauthenticated harness.";

    public AuthorizationDecision Decide(string operation) =>
        AuthorizationDecision.Deny(RejectionReason, DenialKind.Auth);
}

/// <summary>
/// Open authorizer: allows every mutating call. Use ONLY in single-
/// process test harnesses and trusted in-process callers (CLI tools
/// where the operator is already the process owner). NEVER use this
/// behind an HTTP layer that accepts external requests.
/// </summary>
public sealed class AllowAllAuthorizer : IManagementApiAuthorizer
{
    /// <summary>Shared instance — the authorizer holds no state.</summary>
    public static readonly AllowAllAuthorizer Instance = new();

    public AuthorizationDecision Decide(string operation) => AuthorizationDecision.Allow();
}
