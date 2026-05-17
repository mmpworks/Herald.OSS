// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

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
/// <b>Contract.</b> Implementations MUST be side-effect-free and
/// fast — the authorizer is invoked synchronously on the request
/// thread for every mutating call. Authentication, role lookups,
/// and policy evaluation should happen before the request reaches
/// this layer; the authorizer here is the final yes/no gate.
/// </para>
/// </summary>
public interface IManagementApiAuthorizer
{
    /// <summary>
    /// Decide whether the current caller is allowed to perform
    /// <paramref name="operation"/>. Implementations should be
    /// fast and side-effect-free.
    /// </summary>
    /// <param name="operation">
    /// The mutating-method name (e.g. <c>SetMinimumLevel</c>,
    /// <c>CommitFull</c>). Implementations can use this for audit
    /// logging and per-operation policy decisions.
    /// </param>
    /// <param name="reason">
    /// When the result is <c>false</c>, an operator-readable
    /// rejection message that flows into
    /// <see cref="ManagementResult.Fail"/>. When the result is
    /// <c>true</c>, set to <c>null</c>.
    /// </param>
    /// <returns>
    /// <c>true</c> when the operation is allowed; <c>false</c> when
    /// it must be rejected.
    /// </returns>
    bool IsAuthorized(string operation, out string? reason);
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

    public bool IsAuthorized(string operation, out string? reason)
    {
        reason = "HeraldManagementApi is unconfigured: no IManagementApiAuthorizer was supplied. " +
                 "Wire one before exposing this API over HTTP, or pass AllowAllAuthorizer for a " +
                 "deliberately-unauthenticated harness.";
        return false;
    }
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

    public bool IsAuthorized(string operation, out string? reason)
    {
        reason = null;
        return true;
    }
}
