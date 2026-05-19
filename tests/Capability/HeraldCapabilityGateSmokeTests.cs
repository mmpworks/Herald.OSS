#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FluentAssertions;
using MMP.Herald.Configuration;
using MMP.Herald.Pipeline;
using MMP.Herald.Routing;
using Xunit;

namespace MMP.Herald.OSS.Tests.Capability;

/// <summary>
/// G-2 smoke coverage for the capability-composition primitive surfaces in
/// Herald.OSS 0.7.0. Confirms every surface from the dispatch checklist
/// compiles, exposes the documented shape, and observes the documented
/// behaviour on the happy path + the obvious failure path.
///
/// <para>
/// This is NOT the 76-test Echo brief. The Echo brief
/// (<c>docs/_wip/capability-composition-test-matrix-echo.md</c>) is split
/// between G-4 (58 tests, after the catalog + generator land) and G-5
/// (18 tests, after the lifecycle surfaces land). These smoke tests are
/// the "the primitives compile and don't lie" floor under the brief.
/// </para>
///
/// <para>
/// Joined to <see cref="MMP.Herald.OSS.Tests.Helpers.HeraldVersionCollection"/>
/// because these tests mutate process-global state on
/// <see cref="HeraldVersion"/> (<c>CurrentCapabilities</c>,
/// <c>CapabilityResolver</c>, the <c>OnCapabilityChecked</c> event) AND the
/// pre-existing <c>HeraldVersionEditionTests</c> calls
/// <c>HeraldVersion.ResetForTesting()</c> which wipes the cap-set under us.
/// Joining a single collection serialises the two classes without disabling
/// parallelism for the rest of the suite.
/// </para>
/// </summary>
[Collection(MMP.Herald.OSS.Tests.Helpers.HeraldVersionCollection.Name)]
public sealed class HeraldCapabilityGateSmokeTests : IDisposable
{
    public HeraldCapabilityGateSmokeTests()
    {
        // Each test starts from a known-clean process-global state.
        HeraldVersion.ResetForTesting();
    }

    public void Dispose()
    {
        HeraldVersion.ResetForTesting();
    }

    // ── Gate primitives ────────────────────────────────────────────────

    [Fact]
    public void Require_throws_when_capability_absent()
    {
        // No capabilities published; the gate must throw.
        var act = () => HeraldCapabilityGate.Require("multi-tenant", "MyFeature");

        act.Should()
            .Throw<HeraldCapabilityRequirementException>()
            .Where(ex => ex.Capability == "multi-tenant" && ex.Feature == "MyFeature" && ex.TenantId == null);
    }

    [Fact]
    public void Require_succeeds_when_capability_present()
    {
        HeraldVersion.ReplaceCurrentCapabilities(ImmutableHashSet.Create("multi-tenant"));

        var act = () => HeraldCapabilityGate.Require("multi-tenant", "MyFeature");

        act.Should().NotThrow();
    }

    [Fact]
    public void AllowsFeature_returns_bool_and_never_throws()
    {
        HeraldVersion.ReplaceCurrentCapabilities(ImmutableHashSet.Create("compliance-overlay"));

        HeraldCapabilityGate.AllowsFeature("compliance-overlay").Should().BeTrue();
        HeraldCapabilityGate.AllowsFeature("multi-tenant").Should().BeFalse();
    }

    [Fact]
    public void Probe_returns_verdict_without_firing_observation_event()
    {
        HeraldVersion.ReplaceCurrentCapabilities(ImmutableHashSet.Create("ffiec-chain"));

        var fired = 0;
        Action<CapabilityGateEvaluation> handler = _ => fired++;
        HeraldCapabilityGate.OnCapabilityChecked += handler;
        try
        {
            HeraldCapabilityGate.Probe(null, "ffiec-chain").Should().BeTrue();
            HeraldCapabilityGate.Probe(null, "missing").Should().BeFalse();
        }
        finally
        {
            HeraldCapabilityGate.OnCapabilityChecked -= handler;
        }

        fired.Should().Be(0, "Probe is observation-only — must not fire OnCapabilityChecked");
    }

    [Fact]
    public void RequireOn_internal_test_overload_drives_against_explicit_set()
    {
        // Internal overload — lets tests pin the throw/no-throw decision
        // without touching the process-global state.
        var caps = ImmutableHashSet.Create("multi-tenant");

        var noThrow = () => HeraldCapabilityGate.RequireOn(caps, "multi-tenant", "Feature");
        var doesThrow = () => HeraldCapabilityGate.RequireOn(caps, "compliance-overlay", "Feature");

        noThrow.Should().NotThrow();
        doesThrow.Should().Throw<HeraldCapabilityRequirementException>();
    }

    // ── HeraldVersion.CapabilityResolver (Rosanne S-1) ─────────────────

    [Fact]
    public void Default_CapabilityResolver_returns_process_global_cap_set_for_any_tenant_id()
    {
        HeraldVersion.ReplaceCurrentCapabilities(ImmutableHashSet.Create("multi-tenant"));

        HeraldVersion.CapabilityResolver(null).Should().Contain("multi-tenant");
        HeraldVersion.CapabilityResolver("tenant-a").Should().Contain("multi-tenant");
        HeraldVersion.CapabilityResolver("tenant-b").Should().Contain("multi-tenant");
    }

    [Fact]
    public void RequireFor_consults_swapped_resolver_per_tenant()
    {
        HeraldVersion.ReplaceCurrentCapabilities(ImmutableHashSet<string>.Empty);

        // Swap in a per-tenant resolver that grants compliance-overlay
        // to tenant-a only.
        HeraldVersion.CapabilityResolver = tenantId => tenantId switch
        {
            "tenant-a" => (IReadOnlySet<string>)ImmutableHashSet.Create("compliance-overlay"),
            _ => ImmutableHashSet<string>.Empty,
        };

        var tenantAPasses = () => HeraldCapabilityGate.RequireFor("tenant-a", "compliance-overlay", "Audit");
        var tenantBFails = () => HeraldCapabilityGate.RequireFor("tenant-b", "compliance-overlay", "Audit");

        tenantAPasses.Should().NotThrow();
        tenantBFails.Should()
            .Throw<HeraldCapabilityRequirementException>()
            .Where(ex => ex.TenantId == "tenant-b");
    }

    [Fact]
    public void Resolver_assigned_to_null_falls_back_to_default()
    {
        HeraldVersion.ReplaceCurrentCapabilities(ImmutableHashSet.Create("pro-base"));

        // Assigning null must not break the read path — the property
        // setter coerces null to the default resolver.
        HeraldVersion.CapabilityResolver = null!;

        HeraldVersion.CapabilityResolver(null).Should().Contain("pro-base");
    }

    // ── OnCapabilityChecked (Rosanne S-2) ──────────────────────────────

    [Fact]
    public void OnCapabilityChecked_fires_on_both_success_and_failure_paths()
    {
        HeraldVersion.ReplaceCurrentCapabilities(ImmutableHashSet.Create("multi-tenant"));

        var firings = new List<CapabilityGateEvaluation>();
        Action<CapabilityGateEvaluation> handler = ev => firings.Add(ev);
        HeraldCapabilityGate.OnCapabilityChecked += handler;
        try
        {
            // Success path
            HeraldCapabilityGate.Require("multi-tenant", "Feat1");

            // Failure path
            try { HeraldCapabilityGate.Require("compliance-overlay", "Feat2"); }
            catch (HeraldCapabilityRequirementException) { /* expected */ }
        }
        finally
        {
            HeraldCapabilityGate.OnCapabilityChecked -= handler;
        }

        firings.Should().HaveCount(2);
        firings[0].Should().BeEquivalentTo(new
        {
            Capability = "multi-tenant",
            FeatureName = "Feat1",
            TenantId = (string?)null,
            Granted = true,
        }, opts => opts.ExcludingMissingMembers());
        firings[1].Should().BeEquivalentTo(new
        {
            Capability = "compliance-overlay",
            FeatureName = "Feat2",
            TenantId = (string?)null,
            Granted = false,
        }, opts => opts.ExcludingMissingMembers());
    }

    [Fact]
    public void OnCapabilityChecked_handler_throwing_does_not_corrupt_gate_decision()
    {
        HeraldVersion.ReplaceCurrentCapabilities(ImmutableHashSet.Create("multi-tenant"));

        var countingFired = 0;
        Action<CapabilityGateEvaluation> throwingHandler = _ => throw new InvalidOperationException("boom");
        Action<CapabilityGateEvaluation> countingHandler = _ => countingFired++;

        HeraldCapabilityGate.OnCapabilityChecked += throwingHandler;
        HeraldCapabilityGate.OnCapabilityChecked += countingHandler;
        try
        {
            var act = () => HeraldCapabilityGate.Require("multi-tenant", "Feat");
            act.Should().NotThrow("the gate decision is granted; the throwing handler must be isolated");

            countingFired.Should().Be(1, "the counting handler runs even when an earlier handler throws");
        }
        finally
        {
            HeraldCapabilityGate.OnCapabilityChecked -= throwingHandler;
            HeraldCapabilityGate.OnCapabilityChecked -= countingHandler;
        }
    }

    // ── CapabilityRequirement (Rosanne S-3) ────────────────────────────

    [Fact]
    public void CapabilityRequirement_implicit_string_conversion_round_trips()
    {
        // Both forms must produce a structurally equivalent declaration.
        IReadOnlyList<CapabilityRequirement> fromLiteral = ["multi-tenant"];
        IReadOnlyList<CapabilityRequirement> fromCtor = [new CapabilityRequirement("multi-tenant")];

        fromLiteral.Should().HaveCount(1);
        fromCtor.Should().HaveCount(1);
        fromLiteral[0].Name.Should().Be("multi-tenant");
        fromCtor[0].Name.Should().Be("multi-tenant");
        fromLiteral[0].Should().Be(fromCtor[0]);
    }

    [Fact]
    public void CapabilityRequirement_ToString_returns_bare_name_not_record_format()
    {
        var req = new CapabilityRequirement("compliance-overlay");
        req.ToString().Should().Be("compliance-overlay");
    }

    // ── Component-metadata surfaces ────────────────────────────────────

    [Fact]
    public void IComponentMetadata_default_RequiredCapabilities_is_empty()
    {
        // Interface default-implementation: accessed via the interface
        // reference rather than the concrete type.
        IComponentMetadata fake = new FakeComponent();
        fake.RequiredCapabilities.Should().BeEmpty();
    }

    [Fact]
    public void KnownSink_Register_accepts_RequiredCapabilities_overload()
    {
        var unique = "smoketest-cap-sink-" + Guid.NewGuid().ToString("N");
        var sink = KnownSink.Register(unique,
            displayName: "Smoke Test Sink",
            description: "G-2 smoke",
            requiredCapabilities: ["multi-tenant"]);

        sink.RequiredCapabilities.Should().HaveCount(1);
        sink.RequiredCapabilities[0].Name.Should().Be("multi-tenant");
    }

    [Fact]
    public void PipelineStep_Register_cap_monotonicity_carve_out_allows_legacy_to_cap()
    {
        // Echo amendment #2 carve-out: a step previously registered with
        // empty caps (edition-only) may adopt any cap-set on re-register.
        var unique = "smoketest-monotonicity-carveout-" + Guid.NewGuid().ToString("N");

        PipelineStep.Register(unique, displayName: "Step", minimumEdition: HeraldEdition.Pro);
        var act = () => PipelineStep.Register(unique, requiredCapabilities: ["multi-tenant"]);

        act.Should().NotThrow("edition-only prior imposes no cap-monotonicity floor");
        PipelineStep.AllNames.Should().Contain(unique);
    }

    [Fact]
    public void PipelineStep_Register_cap_monotonicity_rejects_subset()
    {
        // Re-register with a strict subset of the prior cap-set —
        // monotonicity violation, must throw.
        var unique = "smoketest-monotonicity-subset-" + Guid.NewGuid().ToString("N");

        PipelineStep.Register(unique,
            displayName: "Step",
            requiredCapabilities: ["multi-tenant", "compliance-overlay"]);

        var act = () => PipelineStep.Register(unique, requiredCapabilities: ["multi-tenant"]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*compliance-overlay*");
    }

    [Fact]
    public void PipelineStep_Register_cap_monotonicity_allows_superset()
    {
        // Re-register with a superset — allowed (the additive expansion path).
        var unique = "smoketest-monotonicity-superset-" + Guid.NewGuid().ToString("N");

        PipelineStep.Register(unique,
            displayName: "Step",
            requiredCapabilities: ["multi-tenant"]);

        var act = () => PipelineStep.Register(unique,
            requiredCapabilities: ["multi-tenant", "compliance-overlay"]);

        act.Should().NotThrow();
    }

    // ── Capability name shape validator (Rosanne #15) ──────────────────

    [Fact]
    public void ValidateCapabilityNameShape_accepts_canonical_and_vendor_prefixed_names()
    {
        HeraldCapabilityGate.ValidateCapabilityNameShape("multi-tenant").Should().BeTrue();
        HeraldCapabilityGate.ValidateCapabilityNameShape("compliance-overlay").Should().BeTrue();
        HeraldCapabilityGate.ValidateCapabilityNameShape("ffiec-chain").Should().BeTrue();
        HeraldCapabilityGate.ValidateCapabilityNameShape("acme.advanced-redaction").Should().BeTrue();
        HeraldCapabilityGate.ValidateCapabilityNameShape("pro-base").Should().BeTrue();
    }

    [Fact]
    public void ValidateCapabilityNameShape_rejects_malformed_names()
    {
        // Uppercase, leading hyphen, trailing hyphen, empty, whitespace,
        // and stray punctuation all fail the convention.
        HeraldCapabilityGate.ValidateCapabilityNameShape("Multi-Tenant").Should().BeFalse();
        HeraldCapabilityGate.ValidateCapabilityNameShape("-leading-hyphen").Should().BeFalse();
        HeraldCapabilityGate.ValidateCapabilityNameShape("trailing-hyphen-").Should().BeFalse();
        HeraldCapabilityGate.ValidateCapabilityNameShape("").Should().BeFalse();
        HeraldCapabilityGate.ValidateCapabilityNameShape("   ").Should().BeFalse();
        HeraldCapabilityGate.ValidateCapabilityNameShape("cap_with_underscore").Should().BeFalse();
        HeraldCapabilityGate.ValidateCapabilityNameShape("cap with space").Should().BeFalse();
    }

    // ── HeraldLicenseRevokedException ──────────────────────────────────

    [Fact]
    public void HeraldLicenseRevokedException_carries_reason_and_license_id_in_message()
    {
        var ex = new HeraldLicenseRevokedException(reasonCode: "dispute", licenseId: "lic_123");

        ex.ReasonCode.Should().Be("dispute");
        ex.LicenseId.Should().Be("lic_123");
        ex.Message.Should().Contain("Revoked")
            .And.Contain("dispute")
            .And.Contain("lic_123")
            .And.Contain("sales@mmpworks.com");
    }

    // ── Test doubles ───────────────────────────────────────────────────

    private sealed class FakeComponent : IComponentMetadata
    {
        public string ComponentName => "fake";
        public string DisplayName => "Fake";
        public string Description => string.Empty;
        public string Help => string.Empty;
        public VendorInfo Vendor => VendorInfo.MMP;
        public IReadOnlyList<SinkConfigField> ConfigurationSchema => [];
    }
}
