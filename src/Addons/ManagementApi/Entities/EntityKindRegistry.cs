#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using MMP.Herald.Addons.ManagementApi.Entities.Policies;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Quick;

namespace MMP.Herald.Addons.ManagementApi.Entities;

/// <summary>
/// Boot-time registry of <see cref="IEntityKindPolicy"/> instances. Built
/// once during Restore; not mutated at runtime. The registry collapses
/// the per-kind restore blocks into a single iteration so adding a new
/// kind becomes "register one policy" instead of "copy-paste a fourth
/// restore block."
///
/// Boot-time validation: see <see cref="FindUnregisteredKindSections"/>.
/// The validator closes the class of bug that hit PropertyStyles before
/// E1 — section serialized on save but no restore block, silently
/// dropped on the next load. With the validator, an unregistered kind
/// surfaces explicitly instead of mutating away the operator's data.
/// </summary>
internal sealed class EntityKindRegistry
{
    // Regex anchored to match the validation rule cited in the design
    // doc's "Open questions → Still genuinely open #1." Named group so
    // the codebase rule against unnamed capture groups stays satisfied
    // even though this pattern has no captures of interest beyond the
    // boolean match.
    private static readonly Regex KindNamePattern = new(
        @"^(?<kind>[a-zA-Z][a-zA-Z0-9_-]+)$",
        RegexOptions.Compiled);

    private readonly Dictionary<string, IEntityKindPolicy> _byKind =
        new(StringComparer.Ordinal);

    public IReadOnlyCollection<IEntityKindPolicy> AllPolicies => _byKind.Values;

    /// <summary>
    /// Register a policy. Validates the Kind name and refuses duplicate
    /// registrations explicitly — silent-overwrite would re-introduce
    /// the PropertyStyles-class bug at a different layer.
    /// </summary>
    public void Register(IEntityKindPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!KindNamePattern.IsMatch(policy.Kind))
            throw new ArgumentException(
                $"Invalid entity kind name '{policy.Kind}'. Must match ^[a-zA-Z][a-zA-Z0-9_-]+$.",
                nameof(policy));
        if (_byKind.ContainsKey(policy.Kind))
            throw new InvalidOperationException(
                $"Entity kind '{policy.Kind}' already registered.");
        _byKind[policy.Kind] = policy;
    }

    /// <summary>
    /// Resolve a policy by Kind. Returns null when the kind is not
    /// registered — callers decide whether that is fatal or recoverable.
    /// </summary>
    public IEntityKindPolicy? Resolve(string kind)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        return _byKind.TryGetValue(kind, out var policy) ? policy : null;
    }

    /// <summary>
    /// Detect kind-shaped sections present in the stored JSON that no
    /// registered policy can restore. Returns the set of well-known
    /// section kinds that are populated but unowned. The caller can log
    /// a warning and preserve those sections opaque (skipping their
    /// restore call) rather than clearing them.
    ///
    /// E1 wires the three style sections; new kinds extend
    /// <see cref="KindShapedSections"/> as E2 / E3 lands their policies.
    /// The list is hardcoded on purpose — boot-time wiring check, not a
    /// plugin discovery surface.
    /// </summary>
    public IReadOnlyList<string> FindUnregisteredKindSections(JsonLoggingConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var orphaned = new List<string>();
        foreach (var (kind, populated) in KindShapedSections(config))
        {
            if (!populated) continue;
            if (!_byKind.ContainsKey(kind)) orphaned.Add(kind);
        }
        return orphaned;
    }

    /// <summary>
    /// Restore every registered policy from the stored JSON. Each policy
    /// owns its own clear-first semantics; the registry just iterates.
    /// Sections that no policy claims are preserved opaque (never
    /// touched). Pair with <see cref="FindUnregisteredKindSections"/> at
    /// the call site to surface those preservation events.
    /// </summary>
    public void RestoreAll(QuickLogBuilder builder, JsonLoggingConfig config)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(config);
        foreach (var policy in _byKind.Values)
            policy.RestoreFromConfig(builder, config);
    }

    /// <summary>
    /// Build the default registry. E1 added the three style kinds; E2
    /// added <c>sink</c> and <c>alias</c>; E3 added <c>enricher</c>.
    /// Decorators and processors are surfaced in
    /// <see cref="KindShapedSections"/> so the boot-time validator
    /// flags populated sections, but they do not register a restore
    /// policy until reconstruction registries (parallel to
    /// <c>EnricherJsonRegistry</c>) ship. Channels follow the same
    /// pattern.
    /// </summary>
    public static EntityKindRegistry CreateDefault()
    {
        var registry = new EntityKindRegistry();
        registry.Register(new LevelStyleEntityPolicy());
        registry.Register(new CategoryStyleEntityPolicy());
        registry.Register(new PropertyStyleEntityPolicy());
        registry.Register(new SinkEntityPolicy());
        registry.Register(new AliasEntityPolicy());
        registry.Register(new EnricherEntityPolicy());
        return registry;
    }

    /// <summary>
    /// Warn about orphaned kind-shaped sections on stderr, where Herald's other
    /// boot-time diagnostics live. NOT <see cref="Debug.WriteLine"/> — that call
    /// is [Conditional("DEBUG")] and compiles out of Release builds entirely,
    /// which made this validator a no-op in every shipped package (the exact
    /// silent-drop it exists to catch). Until L1 brings a structured
    /// observability hook, stderr is the surface operators actually see.
    /// </summary>
    public void WarnOrphanedSections(IReadOnlyList<string> orphaned)
    {
        if (orphaned is null || orphaned.Count == 0) return;
        foreach (var kind in orphaned)
        {
            Console.Error.WriteLine(
                $"[Herald] WARN: boot-time validation: kind-shaped section '{kind}' present in JSON but no IEntityKindPolicy registered — section preserved opaque. " +
                "Source: startup-validation.");
        }
    }

    // Map of well-known kind names to "is the section populated in this
    // JSON." Boot-time wiring check, not plugin discovery — extending
    // this list is a deliberate code change as new kinds migrate.
    //
    // `alias` is detected by walking PipelineSteps for any non-empty
    // Alias property rather than a dedicated JSON list, because aliases
    // ride with their step (see AliasEntityPolicy summary).
    //
    // `decorator`, `processor`, `channel` are listed even though no
    // restore policy is registered yet — the validator should flag
    // those sections as orphaned when a config carries them, so the
    // operator sees the gap rather than silent preservation. The
    // policies register once a reconstruction registry (parallel to
    // <see cref="Enrichers.EnricherJsonRegistry"/>) ships for each.
    private static IEnumerable<(string Kind, bool Populated)> KindShapedSections(JsonLoggingConfig config)
    {
        yield return ("levelStyle", config.LevelStyles is { Count: > 0 });
        yield return ("categoryStyle", config.CategoryStyles is { Count: > 0 });
        yield return ("propertyStyle", config.PropertyStyles is { Count: > 0 });
        yield return ("sink", config.Sinks is { Count: > 0 });
        yield return ("alias", HasAnyAlias(config));
        yield return ("enricher", config.Enrichers is { Count: > 0 });
        yield return ("decorator", config.PipelineDecorators is { Count: > 0 });
        yield return ("processor", config.EventProcessors is { Count: > 0 });
        yield return ("channel", config.Channels is { Count: > 0 });
    }

    private static bool HasAnyAlias(JsonLoggingConfig config)
    {
        if (config.PipelineSteps is null) return false;
        foreach (var step in config.PipelineSteps)
        {
            if (!string.IsNullOrEmpty(step.Alias)) return true;
        }
        return false;
    }
}
