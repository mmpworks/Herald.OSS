#nullable enable

using MMP.Herald.Configuration.Json;
using MMP.Herald.Enrichers;
using MMP.Herald.Quick;

namespace MMP.Herald.Addons.ManagementApi.Entities.Policies;

/// <summary>
/// Enrichers ride a per-entry try / catch: each saved entry routes
/// through <see cref="EnricherJsonRegistry.Reconstruct"/>, and an
/// unknown kind on the way back in (typical when a plugin is not
/// loaded in this host) skips just that entry rather than failing the
/// whole boot. The pattern matches the pre-policy restore block —
/// <see cref="EnricherJsonRegistry"/>'s Reload path throws by design
/// for the loud-failure case; the boot path is more conservative.
///
/// No clear-then-replay: enricher state is owned by the builder's
/// <see cref="QuickLogBuilder.Enrichers"/> set, which the bootstrap
/// constructs fresh per pipeline. Restoring per-entry into an empty
/// set is the right contract.
/// </summary>
internal sealed class EnricherEntityPolicy : IEntityKindPolicy
{
    public string Kind => "enricher";

    public bool HasSectionInConfig(JsonLoggingConfig config) =>
        config.Enrichers is { Count: > 0 };

    public void RestoreFromConfig(QuickLogBuilder builder, JsonLoggingConfig config)
    {
        if (config.Enrichers is null) return;
        foreach (var entry in config.Enrichers)
        {
            try
            {
                var rebuilt = EnricherJsonRegistry.Reconstruct(entry);
                builder.Enrichers.Add(entry.Kind, rebuilt);
            }
            catch
            {
                // Unknown enricher kind — likely a plugin not loaded
                // in this host. Skip rather than block boot. The
                // EnricherJsonRegistry.Reload path throws by design;
                // boot is the conservative caller.
            }
        }
    }
}
