#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using MMP.Herald;
using MMP.Herald.Configuration;
using Xunit;

namespace MMP.Herald.Tests.PipelineCatalog_NS;

/// <summary>
/// Coverage for the unified catalog wiring:
/// <see cref="PipelineCapabilityMatrix"/> extension to strategy steps,
/// <see cref="PipelineManifestReader"/> resource resolution, and
/// <see cref="PipelineCatalog.Build"/> shape stability.
/// </summary>
public sealed class PipelineCatalogTests
{
    // ── Matrix: strategy-step coverage ───────────────────────────────

    [Theory]
    [InlineData("swappable")]
    [InlineData("hotPath")]
    [InlineData("async")]
    [InlineData("rendering")]
    [InlineData("batching")]
    [InlineData("filtering")]
    [InlineData("postFiltering")]
    [InlineData("eventProcessing")]
    [InlineData("flightRecorder")]
    [InlineData("fanOut")]
    public void Every_static_strategy_step_appears_in_matrix(string stepName)
    {
        PipelineCapabilityMatrix.Contains(stepName).Should().BeTrue(
            $"strategy step '{stepName}' must auto-register from PipelineStep.AllNames");

        var entry = PipelineCapabilityMatrix.For(stepName);
        entry.Kind.Should().Be(CapabilityKind.StrategyStep);
        entry.DisplayName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Strategy_step_entries_inherit_tier_from_PipelineStep()
    {
        // Spot-check two Community-tier steps to confirm the tier
        // round-trips through FromStep. Paid-tier steps (audit,
        // circuitBreaker, retry, durableBuffer, fallback) register from
        // Herald.Enterprise/Compliance at plugin load time, so this
        // OSS-only suite never sees them — that round-trip is covered
        // in Modules/Core's test project instead.
        var flightRecorder = PipelineCapabilityMatrix.For("flightRecorder");
        var step = PipelineStep.FromName("flightRecorder");
        step.Should().NotBeNull();
        flightRecorder.TierRequirement.Should().Be(step!.MinimumEdition);

        var swappable = PipelineCapabilityMatrix.For("swappable");
        var stepSwap = PipelineStep.FromName("swappable");
        stepSwap.Should().NotBeNull();
        swappable.TierRequirement.Should().Be(stepSwap!.MinimumEdition);
    }

    [Fact]
    public void Strategy_step_entries_carry_unknown_kernel_cost()
    {
        // Strategy steps don't have kernel-vs-chain head-to-heads — the
        // catalog must say "no number" rather than fabricate one.
        var filtering = PipelineCapabilityMatrix.For("filtering");
        filtering.KernelCost.HasValue.Should().BeFalse(
            "strategy steps live on the chain by definition; kernel cost is Unknown");
    }

    [Fact]
    public void Kernel_companions_keep_their_kind()
    {
        foreach (var key in new[]
        {
            PipelineCapabilityMatrix.Keys.FastRedaction,
            PipelineCapabilityMatrix.Keys.FastSampling,
            PipelineCapabilityMatrix.Keys.FastEnrichment,
            PipelineCapabilityMatrix.Keys.FastDynamicLevel,
            PipelineCapabilityMatrix.Keys.FastAsyncSink,
        })
        {
            PipelineCapabilityMatrix.For(key).Kind
                .Should().Be(CapabilityKind.KernelCompanion);
        }
    }

    [Fact]
    public void ByKind_filters_correctly()
    {
        var companions = PipelineCapabilityMatrix.ByKind(CapabilityKind.KernelCompanion).ToList();
        var steps = PipelineCapabilityMatrix.ByKind(CapabilityKind.StrategyStep).ToList();

        companions.Should().HaveCount(5,
            "five kernel companions seed the matrix today");
        steps.Should().NotBeEmpty();
        (companions.Count + steps.Count).Should().Be(PipelineCapabilityMatrix.All.Count);
    }

    // ── Manifest reader ──────────────────────────────────────────────

    [Theory]
    [InlineData("fastRedaction")]
    [InlineData("fastSampling")]
    [InlineData("fastEnrichment")]
    [InlineData("fastDynamicLevel")]
    [InlineData("fastAsyncSink")]
    public void Kernel_companion_has_a_specific_manifest(string featureKey)
    {
        var capability = PipelineCapabilityMatrix.For(featureKey);
        var text = PipelineManifestReader.ReadForCapability(capability);

        text.Should().NotBeNullOrEmpty(
            $"kernel companion '{featureKey}' must ship its own .mmpform — no fallback for companions");
        text.Should().Contain(capability.DisplayName,
            "the manifest's container header should name the companion so the dashboard's left-side label matches");
    }

    [Fact]
    public void Strategy_step_stub_resource_is_embedded()
    {
        // Every strategy step now ships a bespoke step-{name}.mmpform, so
        // no live capability falls back to the stub on the happy path.
        // The stub is still the contractual safety net for any future
        // step added to the matrix without a per-step form, so it must
        // remain embedded in the assembly.
        var text = PipelineManifestReader.TryReadResource("manifests/pipeline/step-stub.mmpform");

        text.Should().NotBeNullOrEmpty(
            "the stub fallback must always resolve so the dashboard's contract holds for any new step lacking a bespoke form");
        text.Should().Contain("Stub form",
            "the fallback comment header marks it as the stub so the dashboard can show a 'placeholder' badge if it wants");
    }

    [Theory]
    [InlineData("swappable")]
    [InlineData("hotPath")]
    [InlineData("async")]
    [InlineData("rendering")]
    [InlineData("batching")]
    [InlineData("filtering")]
    [InlineData("postFiltering")]
    [InlineData("eventProcessing")]
    [InlineData("flightRecorder")]
    [InlineData("fanOut")]
    public void Every_strategy_step_ships_its_own_form(string stepName)
    {
        var capability = PipelineCapabilityMatrix.For(stepName);
        var text = PipelineManifestReader.ReadForCapability(capability);

        text.Should().NotBeNullOrEmpty(
            $"strategy step '{stepName}' must ship its own step-{stepName}.mmpform so the dashboard form panel shows every config item required to configure the step");
        text.Should().NotContain("Stub form",
            $"strategy step '{stepName}' has a bespoke form — the stub fallback is reserved for steps added later without a per-step file");
        text.Should().Contain(capability.DisplayName,
            "the manifest's container header should name the step so the dashboard's label matches the strategy picker");
    }

    [Fact]
    public void ResourceNameFor_uses_the_kernel_companion_prefix()
    {
        var capability = PipelineCapabilityMatrix.For("fastSampling");
        var name = PipelineManifestReader.ResourceNameFor(capability);

        name.Should().Be("manifests/pipeline/kernel-companion-fastSampling.mmpform");
    }

    [Fact]
    public void ResourceNameFor_uses_the_step_prefix()
    {
        var capability = PipelineCapabilityMatrix.For("filtering");
        var name = PipelineManifestReader.ResourceNameFor(capability);

        name.Should().Be("manifests/pipeline/step-filtering.mmpform");
    }

    [Fact]
    public void TryReadResource_returns_null_for_unknown_name()
    {
        PipelineManifestReader.TryReadResource("manifests/pipeline/does-not-exist.mmpform")
            .Should().BeNull();
    }

    // ── Catalog shape ────────────────────────────────────────────────

    [Fact]
    public void Catalog_includes_every_matrix_entry()
    {
        var catalog = PipelineCatalog.Build();
        // Not an exact-count check: PipelineCapabilityMatrix.All caches a
        // FrozenDictionary at first touch, while PipelineStep.AllNames (and
        // so PipelineCatalog.Build()) stays live. Other test classes in this
        // assembly register throwaway steps via PipelineStep.Register(...);
        // depending on run order those can land in the catalog after the
        // matrix cache has already warmed up. The invariant this test
        // actually owns — every matrix entry has a catalog entry — holds
        // regardless of that ordering.
        var catalogKeys = catalog.Select(c => c.FeatureKey).ToHashSet();
        foreach (var capability in PipelineCapabilityMatrix.All)
        {
            catalogKeys.Should().Contain(capability.FeatureKey);
        }
    }

    [Fact]
    public void Catalog_attaches_form_schema_text_to_every_entry()
    {
        var catalog = PipelineCatalog.Build();
        foreach (var item in catalog)
        {
            item.FormSchemaText.Should().NotBeNullOrEmpty(
                $"catalog item '{item.FeatureKey}' must carry form schema text — kernel companions ship their own; strategy steps fall back to the stub");
        }
    }

    [Fact]
    public void Catalog_kernel_companion_shape_carries_costs()
    {
        var sampling = PipelineCatalog.Build().Single(c => c.FeatureKey == "fastSampling");
        sampling.KernelCost.Should().NotBeNull();
        sampling.ChainCost.Should().NotBeNull();
        sampling.Kind.Should().Be("KernelCompanion");
        sampling.MinimumEdition.Should().Be("Community");
    }

    [Fact]
    public void Catalog_strategy_step_shape_omits_costs_and_carries_help_text()
    {
        var filtering = PipelineCatalog.Build().Single(c => c.FeatureKey == "filtering");
        filtering.KernelCost.Should().BeNull("strategy steps don't carry kernel cost");
        filtering.Help.Should().NotBeNullOrEmpty(
            "strategy steps inherit description+help from PipelineStep so the dashboard can render the help panel");
        filtering.Kind.Should().Be("StrategyStep");
    }

    [Fact]
    public void Catalog_kernel_companion_carries_no_step_metadata()
    {
        // Kernel companions don't have a corresponding PipelineStep, so
        // help / description / vendor on the catalog item are null. The
        // dashboard renders companion-specific help from the
        // capability's UpgradePitch + the manifest text instead.
        var sampling = PipelineCatalog.Build().Single(c => c.FeatureKey == "fastSampling");
        sampling.Description.Should().BeNull();
        sampling.Help.Should().BeNull();
        sampling.UpgradePitch.Should().NotBeNullOrEmpty();
    }
}
