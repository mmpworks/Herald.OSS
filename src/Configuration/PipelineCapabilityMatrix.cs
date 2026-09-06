#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using MMP.Herald.Pipeline;

namespace MMP.Herald.Configuration;

/// <summary>
/// Cost classification for a pipeline capability — wall-clock and
/// allocation per event on a configured-but-active path. Numbers are
/// reference figures from the validation runs cited in
/// <c>Modules/Core/docs/kernel-fast-path-pattern.md</c>; they describe
/// the shape of the cost, not a hard SLO.
/// </summary>
public readonly record struct CapabilityCost(double Nanoseconds, int Bytes)
{
    /// <summary>Sentinel used when no representative figure exists yet.</summary>
    public static CapabilityCost Unknown { get; } = new(Nanoseconds: -1, Bytes: -1);

    /// <summary>True when this cost has a measured value.</summary>
    public bool HasValue => Nanoseconds >= 0 && Bytes >= 0;
}

/// <summary>
/// What happens when an operator configures a feature on a tier that
/// does not include it. The matrix defines the policy per feature so
/// the gate (separate item) can apply consistent semantics without
/// duplicating per-feature decisions.
/// </summary>
public enum CapabilityFallbackStrategy
{
    /// <summary>
    /// No fallback — booting with this feature on a lower tier throws
    /// at startup. Use for compliance/security-critical features whose
    /// silent absence would mislead the operator (audit chain, HMAC).
    /// </summary>
    Throw,

    /// <summary>
    /// Substitutes the closest available implementation on the running
    /// tier. The pipeline boots; the diagnostic surface names the
    /// substitution and points at the original feature as an upgrade.
    /// </summary>
    GracefulDegrade,

    /// <summary>
    /// Feature is silently omitted on lower tiers. Pipeline boots; the
    /// diagnostic surface lists the omission. Use only when the feature
    /// has no degraded equivalent and is purely additive.
    /// </summary>
    Omit,
}

/// <summary>
/// Categorises a matrix entry so consumers (the dashboard catalog,
/// the recommender, diagnostics) can pick the right rendering.
/// </summary>
public enum CapabilityKind
{
    /// <summary>
    /// A kernel-aware companion configured via QuickLogBuilder's
    /// <c>WithFast*</c> methods — sits on <c>StructuredLogger</c> as a
    /// dispatch-side slot, never disqualifies the kernel.
    /// </summary>
    KernelCompanion,

    /// <summary>
    /// A pipeline strategy step (filtering, async, batching, rendering,
    /// fanOut, swappable, eventProcessing, flightRecorder, postFiltering,
    /// etc.) — assembled into the chain pipeline by
    /// <see cref="PipelineStrategy"/>.
    /// </summary>
    StrategyStep,
}

/// <summary>
/// Single matrix entry — describes one pipeline capability's tier
/// requirement, costs, fallback, and operator-facing pitch in one place.
/// </summary>
public sealed record PipelineCapability(
    string FeatureKey,
    string DisplayName,
    CapabilityKind Kind,
    HeraldEdition TierRequirement,
    CapabilityCost KernelCost,
    CapabilityCost? ChainCost,
    bool ForcesChain,
    CapabilityFallbackStrategy FallbackStrategy,
    string UpgradePitch)
{
    /// <summary>
    /// Build an entry whose tier comes from a component implementing
    /// <see cref="IComponentMetadata"/>. Cost data and fallback policy
    /// stay caller-supplied because they aren't on the metadata
    /// interface today.
    /// </summary>
    public static PipelineCapability FromMetadata(
        IComponentMetadata metadata,
        string featureKey,
        CapabilityCost kernelCost,
        CapabilityCost? chainCost = null,
        bool forcesChain = true,
        CapabilityFallbackStrategy fallbackStrategy = CapabilityFallbackStrategy.Throw,
        string upgradePitch = "",
        CapabilityKind kind = CapabilityKind.KernelCompanion)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrEmpty(featureKey);

        return new PipelineCapability(
            FeatureKey: featureKey,
            DisplayName: metadata.DisplayName,
            Kind: kind,
            TierRequirement: metadata.MinimumEdition,
            KernelCost: kernelCost,
            ChainCost: chainCost,
            ForcesChain: forcesChain,
            FallbackStrategy: fallbackStrategy,
            UpgradePitch: upgradePitch);
    }

    /// <summary>
    /// Build a strategy-step entry from a <see cref="PipelineStep"/>.
    /// Tier comes from the step itself; cost data is left
    /// <see cref="CapabilityCost.Unknown"/> because strategy steps
    /// don't have a kernel/chain head-to-head — they are part of the
    /// chain by definition. Operators reading the catalog see the tier
    /// badge and configuration form; the recommender ignores entries
    /// with unknown costs when picking between alternatives.
    /// </summary>
    public static PipelineCapability FromStep(PipelineStep step, string upgradePitch = "")
    {
        ArgumentNullException.ThrowIfNull(step);
        return new PipelineCapability(
            FeatureKey: step.Name,
            DisplayName: step.DisplayName,
            Kind: CapabilityKind.StrategyStep,
            TierRequirement: step.MinimumEdition,
            KernelCost: CapabilityCost.Unknown,
            ChainCost: null,
            ForcesChain: true,
            FallbackStrategy: step.MinimumEdition == HeraldEdition.Community
                ? CapabilityFallbackStrategy.Omit
                : CapabilityFallbackStrategy.Throw,
            UpgradePitch: upgradePitch);
    }
}

/// <summary>
/// Single source of truth for "what does each pipeline capability cost,
/// what tier does it require, and what should a pipeline do when the
/// running tier doesn't include it." Consumed by the recommender, the
/// dashboard catalog endpoint, and the diagnostic shape — all of which
/// would otherwise duplicate the same per-feature constants.
///
/// <para>
/// Static API by deliberate choice — the catalogue is frozen at module
/// initialization and never mutated, so multiple readers share one
/// snapshot without locking. Static here is fine because the data is
/// immutable; mutable static state is the smell, not static itself.
/// </para>
///
/// <para>
/// Reference numbers come from the validation runs cited in
/// <c>Modules/Core/docs/kernel-fast-path-pattern.md</c> — Redaction,
/// Sampling, Enrichment, DynamicLevel, and AsyncSink. Whenever the
/// underlying numbers change (a new run, a regression, an
/// optimisation), the matrix should be updated alongside the doc.
/// </para>
///
/// <para>
/// Strategy steps are sourced from <see cref="PipelineStep.AllNames"/>
/// at build time. Their tier comes from the <c>PipelineStep</c> static
/// definitions, so adding a new step automatically flows through to
/// the catalog without a matrix-side change.
/// </para>
/// </summary>
public static class PipelineCapabilityMatrix
{
    /// <summary>Feature keys for kernel companions — string constants for consumers.</summary>
    public static class Keys
    {
        public const string FastRedaction = "fastRedaction";
        public const string FastSampling = "fastSampling";
        public const string FastEnrichment = "fastEnrichment";
        public const string FastDynamicLevel = "fastDynamicLevel";
        public const string FastAsyncSink = "fastAsyncSink";
    }

    private static readonly FrozenDictionary<string, PipelineCapability> _entries = BuildEntries();

    /// <summary>
    /// Look up a capability by feature key. Throws when the key is not
    /// known — callers should know whether the feature exists; an
    /// unknown key signals a typo or a missing seed entry.
    /// </summary>
    public static PipelineCapability For(string featureKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(featureKey);
        if (_entries.TryGetValue(featureKey, out var entry)) return entry;
        throw new KeyNotFoundException(
            $"No pipeline capability registered for key '{featureKey}'. " +
            $"Known keys: {string.Join(", ", _entries.Keys)}.");
    }

    /// <summary>Look up by key; returns null when the key is unknown.</summary>
    public static PipelineCapability? ForOrNull(string featureKey)
    {
        if (string.IsNullOrEmpty(featureKey)) return null;
        return _entries.TryGetValue(featureKey, out var entry) ? entry : null;
    }

    /// <summary>True when the given feature key has a registered entry.</summary>
    public static bool Contains(string featureKey) =>
        !string.IsNullOrEmpty(featureKey) && _entries.ContainsKey(featureKey);

    /// <summary>Every registered capability, in stable lexical order.</summary>
    public static IReadOnlyCollection<PipelineCapability> All => _entries.Values;

    /// <summary>Every registered feature key, in stable lexical order.</summary>
    public static IReadOnlyCollection<string> FeatureKeys => _entries.Keys;

    /// <summary>Filter the matrix by capability kind.</summary>
    public static IEnumerable<PipelineCapability> ByKind(CapabilityKind kind)
    {
        foreach (var entry in _entries.Values)
        {
            if (entry.Kind == kind) yield return entry;
        }
    }

    private static FrozenDictionary<string, PipelineCapability> BuildEntries()
    {
        var entries = new Dictionary<string, PipelineCapability>(StringComparer.Ordinal);

        SeedKernelCompanions(entries);
        SeedStrategySteps(entries);

        return entries.ToFrozenDictionary(StringComparer.Ordinal);
    }

    // Kernel-fast-path family. Costs are kernel-side accepted-event
    // figures from the cited validation runs; chain costs reflect the
    // legacy alternative on the same workload (see kernel-fast-path-
    // pattern.md). Tier is Community across the board — the family
    // ships as the 1.0 headline and is available everywhere.
    private static void SeedKernelCompanions(IDictionary<string, PipelineCapability> entries)
    {
        Add(entries, new PipelineCapability(
            FeatureKey: Keys.FastRedaction,
            DisplayName: "Fast Redaction",
            Kind: CapabilityKind.KernelCompanion,
            TierRequirement: HeraldEdition.Community,
            KernelCost: new CapabilityCost(Nanoseconds: 92, Bytes: 344),
            ChainCost: new CapabilityCost(Nanoseconds: 869, Bytes: 1648),
            ForcesChain: false,
            FallbackStrategy: CapabilityFallbackStrategy.GracefulDegrade,
            UpgradePitch: "Fast redaction shaves ~89% wall-clock and ~79% allocation off the chain redactor on the configured-but-inert path."));

        Add(entries, new PipelineCapability(
            FeatureKey: Keys.FastSampling,
            DisplayName: "Fast Sampling",
            Kind: CapabilityKind.KernelCompanion,
            TierRequirement: HeraldEdition.Community,
            KernelCost: new CapabilityCost(Nanoseconds: 26, Bytes: 0),
            ChainCost: new CapabilityCost(Nanoseconds: 687, Bytes: 1088),
            ForcesChain: false,
            FallbackStrategy: CapabilityFallbackStrategy.GracefulDegrade,
            UpgradePitch: "Fast sampling drops at ~13 ns / 0 B — cheaper than the no-sampling baseline because there's no event to allocate."));

        Add(entries, new PipelineCapability(
            FeatureKey: Keys.FastEnrichment,
            DisplayName: "Fast Enrichment",
            Kind: CapabilityKind.KernelCompanion,
            TierRequirement: HeraldEdition.Community,
            KernelCost: new CapabilityCost(Nanoseconds: 138, Bytes: 136),
            ChainCost: new CapabilityCost(Nanoseconds: 939, Bytes: 1832),
            ForcesChain: false,
            FallbackStrategy: CapabilityFallbackStrategy.GracefulDegrade,
            UpgradePitch: "Fast enrichment for static properties (host.name, service.name) cuts ~85% wall-clock and ~93% allocation versus the chain enricher."));

        Add(entries, new PipelineCapability(
            FeatureKey: Keys.FastDynamicLevel,
            DisplayName: "Fast Dynamic Level",
            Kind: CapabilityKind.KernelCompanion,
            TierRequirement: HeraldEdition.Community,
            KernelCost: new CapabilityCost(Nanoseconds: 30, Bytes: 0),
            ChainCost: new CapabilityCost(Nanoseconds: 663, Bytes: 1088),
            ForcesChain: false,
            FallbackStrategy: CapabilityFallbackStrategy.GracefulDegrade,
            UpgradePitch: "Fast dynamic level reads through a LogLevelSwitch on the kernel path — ~30 ns / 0 B per accepted event."));

        Add(entries, new PipelineCapability(
            FeatureKey: Keys.FastAsyncSink,
            DisplayName: "Fast Async Sink",
            Kind: CapabilityKind.KernelCompanion,
            TierRequirement: HeraldEdition.Community,
            KernelCost: new CapabilityCost(Nanoseconds: 233, Bytes: 307),
            ChainCost: new CapabilityCost(Nanoseconds: 1002, Bytes: 1221),
            ForcesChain: false,
            FallbackStrategy: CapabilityFallbackStrategy.GracefulDegrade,
            UpgradePitch: "Fast async sink wraps a Channel<LogEvent> on the kernel path — ~77% wall-clock and ~75% allocation reduction per accepted event."));
    }

    // Strategy steps — pulled from PipelineStep at build time. Adding a
    // new step to PipelineStrategy.cs auto-registers it in the matrix.
    // Tier comes from the step's MinimumEdition; cost data is unknown
    // because strategy steps live on the chain by definition.
    private static void SeedStrategySteps(IDictionary<string, PipelineCapability> entries)
    {
        foreach (var name in PipelineStep.AllNames)
        {
            var step = PipelineStep.FromName(name);
            if (step is null) continue;

            // Skip if this name collides with a kernel companion key —
            // the companion entry wins (more specific cost data).
            if (entries.ContainsKey(step.Name)) continue;

            Add(entries, PipelineCapability.FromStep(step));
        }
    }

    private static void Add(
        IDictionary<string, PipelineCapability> entries,
        PipelineCapability capability)
    {
        if (entries.ContainsKey(capability.FeatureKey))
        {
            throw new InvalidOperationException(
                $"Duplicate capability registration for key '{capability.FeatureKey}'. " +
                $"Each feature must appear in the matrix exactly once.");
        }
        entries.Add(capability.FeatureKey, capability);
    }
}
