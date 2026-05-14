#nullable enable

using MMP.Herald.Configuration.Json;
using MMP.Herald.Quick;

namespace MMP.Herald.Addons.ManagementApi.Entities;

/// <summary>
/// Per-kind plumbing: serialize, restore, validate, clear. Closes over the
/// kind's TConfig internally so the <see cref="EntityKindRegistry"/> can
/// iterate without exposing generics.
///
/// E1 introduces only the restore-side methods needed to collapse the
/// style-restore blocks in <c>HeraldManagementApi.RestoreBuilderFromConfig</c>
/// into a single registry loop. The Apply / Remove side (per-key mutation +
/// lease coordination) lands with L1 / L2 when the PolicyResolver chain
/// comes online.
/// </summary>
internal interface IEntityKindPolicy
{
    /// <summary>
    /// Stable identifier for this kind. Validated against
    /// <c>^[a-zA-Z][a-zA-Z0-9_-]+$</c> at registration time.
    /// </summary>
    string Kind { get; }

    /// <summary>
    /// True when the stored JSON has a non-empty section this policy owns.
    /// Used by boot-time validation to detect "section present but no
    /// matching policy registered" — the silent-drop class of bug that
    /// hit PropertyStyles before E1.
    /// </summary>
    bool HasSectionInConfig(JsonLoggingConfig config);

    /// <summary>
    /// Restore the policy's section from JSON onto the builder. No-op when
    /// the section is null. Owns its own clear-first semantics — levels
    /// upsert, categories and properties clear-then-replay.
    /// </summary>
    void RestoreFromConfig(QuickLogBuilder builder, JsonLoggingConfig config);
}
