#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using MMP.Herald;
using MMP.Herald.Configuration;
using MMP.Herald.Pipeline;
using MMP.Herald.Routing;
using Xunit;

namespace MMP.Herald.Tests.PipelineCapabilityMatrixTests_NS;

/// <summary>
/// Coverage for <see cref="PipelineCapabilityMatrix"/> — the single
/// source of truth for per-capability tier, cost, and fallback metadata.
///
/// <para>Themes covered:</para>
/// <list type="bullet">
///   <item><b>Schema</b> — every documented kernel-fast-path companion
///         appears in the matrix exactly once.</item>
///   <item><b>Reading API</b> — For / ForOrNull / Contains return the
///         expected entries and behave correctly on unknown keys.</item>
///   <item><b>Cost shape</b> — kernel cost is populated for every entry
///         and never larger than the corresponding chain cost (the whole
///         point of the family).</item>
///   <item><b>Fallback policy</b> — every kernel-fast-path companion
///         carries a consistent fallback strategy and a non-empty
///         upgrade pitch.</item>
///   <item><b>Round-trip from metadata</b> — building an entry through
///         <see cref="PipelineCapability.FromMetadata"/> picks up the
///         component's <see cref="IComponentMetadata.MinimumEdition"/>
///         and <see cref="IComponentMetadata.DisplayName"/>.</item>
///   <item><b>Edge cases</b> — null / empty keys, unknown keys, and
///         metadata round-trip with a higher-tier component.</item>
/// </list>
/// </summary>
public sealed class PipelineCapabilityMatrixTests
{
    // ── Schema ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PipelineCapabilityMatrix.Keys.FastRedaction)]
    [InlineData(PipelineCapabilityMatrix.Keys.FastSampling)]
    [InlineData(PipelineCapabilityMatrix.Keys.FastEnrichment)]
    [InlineData(PipelineCapabilityMatrix.Keys.FastDynamicLevel)]
    [InlineData(PipelineCapabilityMatrix.Keys.FastAsyncSink)]
    public void Every_kernel_fast_path_companion_is_registered(string featureKey)
    {
        PipelineCapabilityMatrix.Contains(featureKey).Should().BeTrue(
            $"{featureKey} is part of the kernel-fast-path family and must appear in the matrix");
    }

    [Fact]
    public void Each_capability_appears_exactly_once()
    {
        // The All collection's count must match the distinct-key count;
        // the BuildEntries dictionary throws on duplicates, so this is a
        // belt-and-braces check that the seed list does not list one
        // capability twice.
        var all = PipelineCapabilityMatrix.All;
        var distinct = new HashSet<string>(all.Select(c => c.FeatureKey), StringComparer.Ordinal);
        distinct.Count.Should().Be(all.Count,
            "duplicate FeatureKey would mean the seed list is internally inconsistent");
    }

    [Fact]
    public void All_entries_have_non_empty_display_names()
    {
        foreach (var entry in PipelineCapabilityMatrix.All)
        {
            entry.DisplayName.Should().NotBeNullOrWhiteSpace(
                $"capability '{entry.FeatureKey}' must have an operator-facing display name");
        }
    }

    [Fact]
    public void Kernel_companion_entries_have_non_empty_upgrade_pitches()
    {
        // Strategy steps default to an empty upgrade pitch — they don't
        // have a kernel/chain head-to-head to advertise. Companions must.
        foreach (var entry in PipelineCapabilityMatrix.ByKind(CapabilityKind.KernelCompanion))
        {
            entry.UpgradePitch.Should().NotBeNullOrWhiteSpace(
                $"kernel companion '{entry.FeatureKey}' must carry an operator-readable upgrade pitch");
        }
    }

    // ── Reading API ───────────────────────────────────────────────────

    [Fact]
    public void For_returns_the_expected_entry()
    {
        var entry = PipelineCapabilityMatrix.For(PipelineCapabilityMatrix.Keys.FastSampling);

        entry.Should().NotBeNull();
        entry.FeatureKey.Should().Be(PipelineCapabilityMatrix.Keys.FastSampling);
        entry.TierRequirement.Should().Be(HeraldEdition.Community);
    }

    [Fact]
    public void For_throws_on_unknown_key()
    {
        var act = () => PipelineCapabilityMatrix.For("nope-not-a-real-feature");
        act.Should().Throw<KeyNotFoundException>()
            .WithMessage("*nope-not-a-real-feature*");
    }

    [Fact]
    public void For_rejects_null_or_empty_key()
    {
        var actNull = () => PipelineCapabilityMatrix.For(null!);
        actNull.Should().Throw<ArgumentException>();

        var actEmpty = () => PipelineCapabilityMatrix.For(string.Empty);
        actEmpty.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ForOrNull_returns_null_on_unknown_key()
    {
        PipelineCapabilityMatrix.ForOrNull("does-not-exist").Should().BeNull();
        PipelineCapabilityMatrix.ForOrNull(null!).Should().BeNull();
        PipelineCapabilityMatrix.ForOrNull(string.Empty).Should().BeNull();
    }

    [Fact]
    public void ForOrNull_returns_the_entry_on_known_key()
    {
        var entry = PipelineCapabilityMatrix.ForOrNull(PipelineCapabilityMatrix.Keys.FastEnrichment);
        entry.Should().NotBeNull();
        entry!.FeatureKey.Should().Be(PipelineCapabilityMatrix.Keys.FastEnrichment);
    }

    [Fact]
    public void Contains_handles_known_unknown_and_invalid_keys()
    {
        PipelineCapabilityMatrix.Contains(PipelineCapabilityMatrix.Keys.FastRedaction)
            .Should().BeTrue();
        PipelineCapabilityMatrix.Contains("not-real").Should().BeFalse();
        PipelineCapabilityMatrix.Contains(null!).Should().BeFalse();
        PipelineCapabilityMatrix.Contains(string.Empty).Should().BeFalse();
    }

    [Fact]
    public void FeatureKeys_collection_is_consistent_with_All()
    {
        var keys = new HashSet<string>(PipelineCapabilityMatrix.FeatureKeys, StringComparer.Ordinal);
        var allKeys = new HashSet<string>(
            PipelineCapabilityMatrix.All.Select(c => c.FeatureKey),
            StringComparer.Ordinal);

        keys.Should().BeEquivalentTo(allKeys,
            "the FeatureKeys collection must mirror the All collection's keys");
    }

    // ── Cost shape ────────────────────────────────────────────────────

    [Fact]
    public void Every_kernel_companion_has_a_known_kernel_cost()
    {
        // The whole point of the matrix is to surface measured kernel
        // costs. An Unknown cost on a kernel companion means the seed
        // entry forgot to populate it. Strategy steps are exempt —
        // they have no kernel/chain head-to-head.
        foreach (var entry in PipelineCapabilityMatrix.ByKind(CapabilityKind.KernelCompanion))
        {
            entry.KernelCost.HasValue.Should().BeTrue(
                $"kernel companion '{entry.FeatureKey}' must carry a measured kernel cost");
            entry.KernelCost.Nanoseconds.Should().BeGreaterThan(0,
                $"kernel companion '{entry.FeatureKey}' kernel ns should be a positive measurement");
        }
    }

    [Fact]
    public void Kernel_cost_is_at_most_chain_cost_when_both_are_known()
    {
        // The matrix exists to advertise the kernel-fast-path family
        // BEATING the chain. An entry whose kernel cost is higher than
        // its chain cost would silently mislead the recommender.
        foreach (var entry in PipelineCapabilityMatrix.All)
        {
            if (entry.ChainCost is not { HasValue: true } chainCost) continue;

            entry.KernelCost.Nanoseconds.Should().BeLessThanOrEqualTo(
                chainCost.Nanoseconds,
                $"capability '{entry.FeatureKey}': kernel ns must not exceed chain ns");
            entry.KernelCost.Bytes.Should().BeLessThanOrEqualTo(
                chainCost.Bytes,
                $"capability '{entry.FeatureKey}': kernel bytes must not exceed chain bytes");
        }
    }

    [Fact]
    public void Capability_cost_unknown_sentinel_is_not_HasValue()
    {
        CapabilityCost.Unknown.HasValue.Should().BeFalse();

        var fresh = new CapabilityCost(Nanoseconds: 0, Bytes: 0);
        fresh.HasValue.Should().BeTrue("zero-cost is a valid measurement; -1 is the sentinel");
    }

    // ── Fallback policy ───────────────────────────────────────────────

    [Fact]
    public void Kernel_fast_path_family_uses_graceful_degrade_fallback()
    {
        // Each kernel companion has a chain-side equivalent that always
        // works. If a future tier shrinks the kernel companion to a
        // higher edition, the fallback is the chain — not a startup
        // throw. This pin-test guards that policy.
        string[] kernelKeys =
        {
            PipelineCapabilityMatrix.Keys.FastRedaction,
            PipelineCapabilityMatrix.Keys.FastSampling,
            PipelineCapabilityMatrix.Keys.FastEnrichment,
            PipelineCapabilityMatrix.Keys.FastDynamicLevel,
            PipelineCapabilityMatrix.Keys.FastAsyncSink,
        };
        foreach (var key in kernelKeys)
        {
            var entry = PipelineCapabilityMatrix.For(key);
            entry.FallbackStrategy.Should().Be(CapabilityFallbackStrategy.GracefulDegrade,
                $"{key} has a chain-side equivalent; fallback must be GracefulDegrade, not Throw or Omit");
        }
    }

    [Fact]
    public void Kernel_fast_path_family_does_not_force_chain()
    {
        // Kernel companions are kernel-aware by design and must not
        // force the chain. Strategy steps live on the chain by
        // definition (their ForcesChain is true) — they're exempt.
        foreach (var entry in PipelineCapabilityMatrix.ByKind(CapabilityKind.KernelCompanion))
        {
            entry.ForcesChain.Should().BeFalse(
                $"kernel companion '{entry.FeatureKey}' is kernel-aware by design — it must not force the chain");
        }
    }

    // ── Round-trip from IComponentMetadata ────────────────────────────

    [Fact]
    public void FromMetadata_picks_up_minimum_edition()
    {
        var metadata = new FakeComponentMetadata(
            componentName: "fakeComponent",
            displayName: "Fake Component",
            minimumEdition: HeraldEdition.Pro);

        var entry = PipelineCapability.FromMetadata(
            metadata,
            featureKey: "fakeComponent.feature",
            kernelCost: new CapabilityCost(50, 100),
            chainCost: new CapabilityCost(500, 1000),
            forcesChain: false,
            fallbackStrategy: CapabilityFallbackStrategy.GracefulDegrade,
            upgradePitch: "Fake feature pitch");

        entry.TierRequirement.Should().Be(HeraldEdition.Pro,
            "tier must be sourced from IComponentMetadata.MinimumEdition");
        entry.DisplayName.Should().Be("Fake Component");
        entry.FeatureKey.Should().Be("fakeComponent.feature");
        entry.KernelCost.Should().Be(new CapabilityCost(50, 100));
        entry.ForcesChain.Should().BeFalse();
        entry.FallbackStrategy.Should().Be(CapabilityFallbackStrategy.GracefulDegrade);
    }

    [Fact]
    public void FromMetadata_round_trips_each_edition_correctly()
    {
        // Cover all three editions to guard against an accidental
        // hard-coded default.
        foreach (var edition in new[] { HeraldEdition.Community, HeraldEdition.Pro, HeraldEdition.Enterprise })
        {
            var metadata = new FakeComponentMetadata(
                componentName: $"comp-{edition.Name}",
                displayName: $"Comp {edition.Name}",
                minimumEdition: edition);

            var entry = PipelineCapability.FromMetadata(
                metadata,
                featureKey: $"feature-{edition.Name}",
                kernelCost: new CapabilityCost(100, 200));

            entry.TierRequirement.Should().Be(edition,
                $"FromMetadata must round-trip {edition.Name} faithfully");
        }
    }

    [Fact]
    public void FromMetadata_rejects_null_metadata()
    {
        var act = () => PipelineCapability.FromMetadata(
            metadata: null!,
            featureKey: "any",
            kernelCost: CapabilityCost.Unknown);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FromMetadata_rejects_null_or_empty_feature_key()
    {
        var metadata = new FakeComponentMetadata("c", "C", HeraldEdition.Community);

        var actNull = () => PipelineCapability.FromMetadata(
            metadata, featureKey: null!, kernelCost: CapabilityCost.Unknown);
        actNull.Should().Throw<ArgumentException>();

        var actEmpty = () => PipelineCapability.FromMetadata(
            metadata, featureKey: string.Empty, kernelCost: CapabilityCost.Unknown);
        actEmpty.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromMetadata_uses_default_throw_fallback_when_unspecified()
    {
        var metadata = new FakeComponentMetadata("c", "C", HeraldEdition.Enterprise);

        var entry = PipelineCapability.FromMetadata(
            metadata,
            featureKey: "x",
            kernelCost: new CapabilityCost(1, 1));

        entry.FallbackStrategy.Should().Be(CapabilityFallbackStrategy.Throw,
            "the safer default for a Pro/Enterprise feature whose policy is unspecified is Throw — " +
            "operators discover misconfiguration at startup, not silently at runtime");
        entry.ForcesChain.Should().BeTrue("default forcesChain is true for unknown features");
    }

    // ── Integration with Editions ─────────────────────────────────────

    [Fact]
    public void Community_running_tier_includes_every_kernel_companion()
    {
        // Pin: the kernel-fast-path family ships in Community. Any
        // future tier change is the kind of decision that should fail
        // this test — forcing the change to be deliberate. Strategy
        // steps span all three editions (Audit is Pro, eventProcessing
        // is Pro, etc.) so they're exempt from this test.
        foreach (var entry in PipelineCapabilityMatrix.ByKind(CapabilityKind.KernelCompanion))
        {
            HeraldEdition.Community.Includes(entry.TierRequirement).Should().BeTrue(
                $"kernel companion '{entry.FeatureKey}' must be Community-available; running tier check failed");
        }
    }

    // ── Test fixture ──────────────────────────────────────────────────

    /// <summary>
    /// Minimal IComponentMetadata stand-in for the round-trip tests.
    /// Real components attach this on their concrete class; the matrix
    /// only cares about <c>DisplayName</c> and <c>MinimumEdition</c>.
    /// </summary>
    private sealed class FakeComponentMetadata : IComponentMetadata
    {
        public FakeComponentMetadata(string componentName, string displayName, HeraldEdition minimumEdition)
        {
            ComponentName = componentName;
            DisplayName = displayName;
            MinimumEdition = minimumEdition;
        }

        public string ComponentName { get; }
        public string DisplayName { get; }
        public string Description => string.Empty;
        public string Help => string.Empty;
        public VendorInfo Vendor => VendorInfo.MMP;
        public IReadOnlyList<SinkConfigField> ConfigurationSchema { get; } = Array.Empty<SinkConfigField>();
        public HeraldEdition MinimumEdition { get; }
    }
}
