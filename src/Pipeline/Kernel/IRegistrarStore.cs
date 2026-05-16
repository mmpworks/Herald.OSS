#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Persistence boundary for the <see cref="ExternalSourceRegistrar"/>.
/// The registrar calls <see cref="Load"/> once at bootstrap and
/// <see cref="Save"/> after every successful registration or
/// revocation. Implementations are responsible for atomic writes and
/// transport-specific concerns (file permissions, vault auth).
///
/// <para>
/// The interface is sync because registration is a control-plane
/// operation, not a per-event hot path. A vault implementation that
/// blocks on its async client during <see cref="Save"/> is acceptable —
/// it serializes through the registrar's lock and never sits on the
/// dispatch path.
/// </para>
///
/// <para>
/// <b>Failure semantics.</b> When <see cref="Save"/> throws, the
/// registrar rolls back the in-memory mutation that triggered the call
/// and propagates the exception. Persistence and runtime state stay
/// consistent — there is no "registered in memory but not on disk"
/// state visible to callers. <see cref="Load"/> is allowed to throw if
/// the persisted state is corrupt or unreadable; the bootstrap
/// surfaces the error rather than silently starting fresh.
/// </para>
/// </summary>
public interface IRegistrarStore
{
    /// <summary>
    /// Read the persisted snapshot. Returns null when no snapshot exists
    /// (first boot, store cleared). Throws when the store is reachable
    /// but the data is corrupt — callers must surface that to the
    /// operator rather than silently regenerating.
    /// </summary>
    RegistrarSnapshot? Load();

    /// <summary>
    /// Persist the current snapshot. Implementations should write
    /// atomically (temp + rename for files, conditional update for
    /// vaults) so a crash mid-write doesn't leave the store in a
    /// half-written state.
    /// </summary>
    void Save(RegistrarSnapshot snapshot);
}

/// <summary>
/// One captured registrar state. Carries the schema version, the
/// pipeline's reference token, and every active registration.
///
/// <para>
/// <b>Schema version.</b> Bumps when the persisted shape changes
/// incompatibly. Loaders refuse to read a higher version than they
/// understand and surface a clear error — that protects an operator
/// who downgrades after writing a newer-format snapshot. Older
/// versions can be upgraded in place when the loader knows how.
/// </para>
/// </summary>
public sealed record RegistrarSnapshot(
    int SchemaVersion,
    string ReferenceToken,
    IReadOnlyList<RegistrarPersistedEntry> Registrations)
{
    /// <summary>
    /// Current schema version. Bump when the on-disk shape changes.
    /// </summary>
    public const int CurrentSchemaVersion = 1;
}

/// <summary>
/// One persisted external-source registration. Carries enough to
/// recreate the same derived key on the next boot — the reference
/// token + raw key + timestamp produce a deterministic HMAC, so
/// replaying these entries onto fresh sink gates restores exactly
/// the registration the registrar issued before the restart.
/// </summary>
public sealed record RegistrarPersistedEntry(
    string RawKey,
    long Timestamp,
    IReadOnlyList<string> SinkAliases);
