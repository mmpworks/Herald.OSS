#nullable enable

using System;

namespace MMP.Herald.Addons.Archive;

/// <summary>
/// Outcome of a single <see cref="IArchiveProvider.ArchiveAsync"/> call.
/// Carries the remote identifier (path/key/URI the operator can paste into
/// dashboards) and the SHA-256 checksum the orchestrator persists in the
/// crash-safe checkpoint for verification.
///
/// <para>
/// On failure, <see cref="Success"/> is false, <see cref="Error"/> carries
/// the exception (or null if the provider returned a non-exception
/// failure), and the orchestrator leaves the local file in place for the
/// next scheduled retry.
/// </para>
/// </summary>
public sealed record ArchiveResult(
    bool Success,
    string? RemoteIdentifier,
    string? Sha256,
    Exception? Error)
{
    /// <summary>Build a success result.</summary>
    public static ArchiveResult Ok(string remoteIdentifier, string sha256) =>
        new(true, remoteIdentifier, sha256, Error: null);

    /// <summary>Build a failure result from an exception.</summary>
    public static ArchiveResult Fail(Exception error) =>
        new(false, RemoteIdentifier: null, Sha256: null, error);
}
