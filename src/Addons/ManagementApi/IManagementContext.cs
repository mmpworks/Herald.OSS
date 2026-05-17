// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using MMP.Herald.Configuration;
using MMP.Herald.Quick;

namespace MMP.Herald.Addons.ManagementApi;

/// <summary>
/// The shared mutable state and helpers that every per-axis management
/// class (<see cref="SinkManagement"/>, <see cref="LevelManagement"/>,
/// <see cref="PolicyManagement"/>, <see cref="TransactionScope"/>) reads
/// or writes. The facade (<see cref="HeraldManagementApi"/>) owns the
/// state; the axis classes reach it through this interface so they stay
/// free of cross-axis coupling.
///
/// <para><b>Why an interface and not direct references.</b> Three reasons.
/// (1) The transaction flag and the persist target both belong to the
/// facade — passing the facade reference would let every axis call back
/// into every other axis, recreating the god-class shape this split
/// removes. (2) The authorize-then-dispatch contract Glenn 1 landed
/// (queue #1) needs to wrap every mutation; centralizing the call
/// through the context keeps the gate consistent across axes. (3) The
/// persist plumbing Glenn 1 landed (queue #3) returns a captured
/// exception each axis must surface through <see cref="ManagementResult"/>;
/// the formatter belongs on the context so each axis stays free of
/// duplicate path-formatting logic.</para>
/// </summary>
internal interface IManagementContext
{
    /// <summary>The shared builder every axis mutates.</summary>
    QuickLogBuilder Builder { get; }

    /// <summary>The live pipeline result every axis reads / commits to.</summary>
    QuickLogResult Result { get; }

    /// <summary>
    /// Registration handle, when the API was created from a registry
    /// entry. Some axes (transaction commit, downtime rebuild) consult
    /// this to decide whether a rebuild-with-downtime path is available.
    /// Null when the API was constructed directly.
    /// </summary>
    HeraldRegistration? Registration { get; }

    /// <summary>
    /// Replace the result reference (downtime rebuild). The transaction
    /// axis is the only writer — exposed on the context so the facade
    /// sees the swap too and subsequent reads pick up the new pipeline.
    /// </summary>
    void ReplaceResult(QuickLogResult newResult);

    /// <summary>Whether a transaction is currently active on the facade.</summary>
    bool InTransaction { get; }

    /// <summary>Set / clear the transaction flag. Transaction axis only.</summary>
    void SetInTransaction(bool value);

    /// <summary>The current transaction snapshot JSON, or null when no transaction is active.</summary>
    string? Snapshot { get; set; }

    /// <summary>
    /// The on-disk path the configuration is persisted to, or null
    /// when the API is running detached from a registry.
    /// </summary>
    string? ConfigPath { get; }

    /// <summary>
    /// The directory file-sink paths must resolve within. Null leaves
    /// the legacy pass-through path in place (with a runtime-notice
    /// warning emitted on every confine call).
    /// </summary>
    string? LogRootDirectory { get; }

    /// <summary>
    /// Run the authorize-then-dispatch gate Glenn 1 landed. Returns
    /// <c>null</c> when the operation is allowed; returns a populated
    /// <see cref="ManagementResult.Fail"/> when denied so the caller
    /// can early-return.
    /// </summary>
    ManagementResult? EnsureAuthorized(string operation);

    /// <summary>
    /// Persist the builder's current config to <see cref="ConfigPath"/>
    /// when set. Returns <c>null</c> on success or no-op, or the
    /// captured exception when the disk write failed. Glenn 1's queue
    /// #3 plumbing — every caller surfaces failure through
    /// <see cref="ManagementResult.Fail"/>.
    /// </summary>
    Exception? PersistConfig();

    /// <summary>
    /// Operator-readable summary for a persistence failure exception.
    /// The path comes first so logs and dashboards can group by
    /// destination file.
    /// </summary>
    string FormatPersistFailure(Exception ex);

    /// <summary>
    /// Confine a file-sink path through <see cref="LogRootDirectory"/>.
    /// Returns the resolved-and-confined absolute path. Throws
    /// <see cref="InvalidOperationException"/> on an escape; the
    /// per-axis caller catches that and translates to
    /// <see cref="ManagementResult.Fail"/>.
    /// </summary>
    string ResolveFileSinkPath(string path);

    /// <summary>
    /// If in a transaction, stage the change; otherwise auto-commit
    /// (validate, persist, hot-swap, downtime-rebuild fallback). One
    /// place every per-axis mutator routes through so the commit
    /// contract is identical everywhere.
    /// </summary>
    ManagementResult AutoCommitOrStage(string message);

    /// <summary>
    /// Rebuild with downtime when hot-swap is unavailable but a
    /// registration is attached. Transaction axis delegates here from
    /// its commit path, and <see cref="AutoCommitOrStage"/> falls
    /// through here when the hot-swap returns false.
    /// </summary>
    ManagementResult RebuildWithDowntime();

    /// <summary>
    /// Restore scalar builder properties from a JSON config string.
    /// Used by the transaction-rollback path and by
    /// <c>ApplyConfigJson</c>. The implementation forwards to the
    /// public <see cref="HeraldManagementApi.RestoreBuilderFromConfig"/>.
    /// </summary>
    void RestoreFromJson(string json);
}
