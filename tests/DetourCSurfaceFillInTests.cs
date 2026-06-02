#nullable enable

using System;
using System.Linq;
using FluentAssertions;
using MMP.Herald;
using MMP.Herald.Configuration;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Pipeline;
using Xunit;

namespace MMP.Herald.OSS.Tests;

/// <summary>
/// Echo's pin set for the Detour C exhaustive surface fill-in (spec at
/// herald/docs/_wip/detour-c-surface-fill-in.md, Section 5). One test per
/// confirmed addition — the pins are deliberately small and behavioural,
/// not exhaustive characterisation, because each surface item already has
/// rich coverage elsewhere in the sibling test suite. The intent here is
/// to fire when a future edit silently drops one of the additions.
///
/// <para>
/// SetEdition / ResetForTesting mutate the process-global edition slot on
/// <see cref="HeraldVersion"/>, so this class joins
/// <see cref="MMP.Herald.OSS.Tests.Helpers.EditionStateCollection"/> to
/// serialise against sibling edition mutators at the class level.
/// </para>
/// </summary>
[Collection(MMP.Herald.OSS.Tests.Helpers.EditionStateCollection.Name)]
public sealed class DetourCSurfaceFillInTests
{
    // ── 1. HeraldVersion.Edition ──────────────────────────────────────

    /// <summary>
    /// After SetEdition(Pro), the convenience string getter
    /// HeraldVersion.Edition mirrors CurrentEdition.Name.
    /// </summary>
    [Fact]
    public void HeraldVersion_Edition_TracksCurrentEditionName()
    {
        HeraldVersion.ResetForTesting();
        try
        {
            HeraldVersion.Edition.Should().Be("Community");

            HeraldVersion.SetEdition(HeraldEdition.Pro);

            HeraldVersion.Edition.Should().Be("Pro");
        }
        finally
        {
            HeraldVersion.ResetForTesting();
        }
    }

    // ── 2. HeraldEditionGate.AllowsFeature ────────────────────────────

    /// <summary>
    /// AllowsFeature is the non-throwing query path used by soft-fallback
    /// callers. It must return true when current rank >= minimum and
    /// false when below.
    /// </summary>
    [Fact]
    public void HeraldEditionGate_AllowsFeature_ComparesRanks()
    {
        HeraldVersion.ResetForTesting();
        try
        {
            HeraldEditionGate.AllowsFeature(HeraldEdition.Community).Should().BeTrue();
            HeraldEditionGate.AllowsFeature(HeraldEdition.Pro).Should().BeFalse();

            HeraldVersion.SetEdition(HeraldEdition.Enterprise);

            HeraldEditionGate.AllowsFeature(HeraldEdition.Community).Should().BeTrue();
            HeraldEditionGate.AllowsFeature(HeraldEdition.Pro).Should().BeTrue();
            HeraldEditionGate.AllowsFeature(HeraldEdition.Enterprise).Should().BeTrue();
        }
        finally
        {
            HeraldVersion.ResetForTesting();
        }
    }

    // ── 2b. HeraldEditionGate.Require throws ──────────────────────────

    /// <summary>
    /// Require throws HeraldEditionRequirementException when the running
    /// edition is below the requested minimum. The thrown type is
    /// sibling-owned by deliberate design — sibling Herald.OSS does NOT
    /// take a forward dependency on a licensing assembly.
    /// </summary>
    [Fact]
    public void HeraldEditionGate_Require_Throws_BelowMinimum()
    {
        HeraldVersion.ResetForTesting();
        try
        {
            var act = () => HeraldEditionGate.Require(HeraldEdition.Pro, "AuditChain");

            act.Should().Throw<HeraldEditionRequirementException>()
                .Which.Feature.Should().Be("AuditChain");
        }
        finally
        {
            HeraldVersion.ResetForTesting();
        }
    }

    // ── 3. HeraldEditionRequirementException construction ─────────────

    /// <summary>
    /// The exception carries the three fields a 4xx response handler
    /// needs to render a useful diagnostic: feature, required edition,
    /// current edition.
    /// </summary>
    [Fact]
    public void HeraldEditionRequirementException_CarriesFeatureAndEditions()
    {
        var ex = new HeraldEditionRequirementException(
            feature: "DurableBuffer",
            required: HeraldEdition.Pro,
            current: HeraldEdition.Community);

        ex.Feature.Should().Be("DurableBuffer");
        ex.Required.Should().BeSameAs(HeraldEdition.Pro);
        ex.Current.Should().BeSameAs(HeraldEdition.Community);
        ex.Message.Should().Contain("DurableBuffer")
            .And.Contain("Pro")
            .And.Contain("Community");
    }

    // ── 4. KnownSink.MinimumEdition round-trip ────────────────────────

    /// <summary>
    /// Register with edition + read back via MinimumEdition. Built-in
    /// sinks default to Community when no edition is supplied.
    /// </summary>
    [Fact]
    public void KnownSink_MinimumEdition_RoundTrip_ViaRegister()
    {
        var registered = KnownSink.Register(
            kind: "detour-c-test-sink",
            displayName: "Detour C Test Sink",
            minimumEdition: HeraldEdition.Pro);

        registered.MinimumEdition.Should().BeSameAs(HeraldEdition.Pro);

        // Built-in sinks default to Community.
        KnownSink.Console.MinimumEdition.Should().BeSameAs(HeraldEdition.Community);
    }

    // ── 5. PipelineStep.MinimumEdition round-trip ─────────────────────

    /// <summary>
    /// Register with edition + read back via MinimumEdition. Built-in
    /// steps default to Community.
    /// </summary>
    [Fact]
    public void PipelineStep_MinimumEdition_RoundTrip_ViaRegister()
    {
        var registered = PipelineStep.Register(
            name: "detour-c-test-step",
            displayName: "Detour C Test Step",
            minimumEdition: HeraldEdition.Enterprise);

        registered.MinimumEdition.Should().BeSameAs(HeraldEdition.Enterprise);

        // Built-in steps default to Community.
        PipelineStep.Async.MinimumEdition.Should().BeSameAs(HeraldEdition.Community);
    }

    // ── 6. IComponentMetadata.MinimumEdition default member ───────────

    /// <summary>
    /// The interface ships a default-implemented MinimumEdition returning
    /// Community, so existing sibling implementers (FilteringLogger,
    /// HotPathLogger, etc.) compile without explicit member and the
    /// default reports Community at runtime. Paid implementers override.
    /// </summary>
    [Fact]
    public void IComponentMetadata_MinimumEdition_DefaultsToCommunity()
    {
        IComponentMetadata sample = new MinimalMetadataStub();
        sample.MinimumEdition.Should().BeSameAs(HeraldEdition.Community);
    }

    private sealed class MinimalMetadataStub : IComponentMetadata
    {
        public string ComponentName => "stub";
        public string DisplayName => "Stub";
        public string Description => "Test stub";
        public string Help => "Test stub component";
        public VendorInfo Vendor => VendorInfo.MMP;
        public System.Collections.Generic.IReadOnlyList<MMP.Herald.Routing.SinkConfigField> ConfigurationSchema =>
            Array.Empty<MMP.Herald.Routing.SinkConfigField>();
    }

    // ── 7. PipelineCatalog snapshot shape ─────────────────────────────

    /// <summary>
    /// Build() returns one item per kernel companion (5) plus one item
    /// per registered strategy step name. The 5 fast-path companion
    /// feature keys must all appear so the dashboard catalog endpoint
    /// renders the kernel-fast-path family.
    /// </summary>
    [Fact]
    public void PipelineCatalog_Build_IncludesKernelCompanionsAndStrategySteps()
    {
        var items = PipelineCatalog.Build();

        items.Should().NotBeEmpty();

        var keys = items.Select(i => i.FeatureKey).ToArray();
        keys.Should().Contain(new[]
        {
            "fastRedaction",
            "fastSampling",
            "fastEnrichment",
            "fastDynamicLevel",
            "fastAsyncSink",
        });

        // At least the built-in strategy step names should also be present.
        foreach (var name in PipelineStep.AllNames)
        {
            // KernelCompanion keys may collide with step names; the
            // catalog deduplicates by emitting the companion entry. The
            // weaker pin (item exists with feature key OR companion key)
            // matches the shipping behaviour.
            keys.Should().Contain(name,
                because: "every registered strategy step name should be reachable from the catalog");
        }

        // Each item carries a serialisable Kind discriminator.
        items.Should().OnlyContain(i => i.Kind == "KernelCompanion" || i.Kind == "StrategyStep");
    }

    // ── 8. LoggingJsonSerializer.Deserialize(string) ──────────────────

    /// <summary>
    /// The single-arg overload exists as a distinct metadata entry
    /// (ApiCompat distinguishes it from the optional-bool 2-arg form)
    /// and behaves identically to Deserialize(json, expandEnv: true).
    /// </summary>
    [Fact]
    public void LoggingJsonSerializer_SingleArgDeserialize_MatchesDefaultTwoArg()
    {
        // Minimal config that exercises the deserialiser without
        // depending on incidental schema fields. The single-arg overload
        // must produce a config equal in shape to the 2-arg form when
        // expandEnv is at its default (true).
        const string json = """
            {
              "levels": { "baseLevels": [], "additionalLevels": [], "placements": [] },
              "minimumLevel": "info",
              "async": { "enabled": false },
              "dumpRegisteredLevelsToConsole": false,
              "aliases": [],
              "levelStyles": [],
              "sinks": [],
              "routes": []
            }
            """;

        var single = LoggingJsonSerializer.Deserialize(json);
        var twoArg = LoggingJsonSerializer.Deserialize(json, expandEnv: true);

        single.Should().NotBeNull();
        single.MinimumLevel.Should().Be(twoArg.MinimumLevel);
        single.Async.Enabled.Should().Be(twoArg.Async.Enabled);
    }
}
