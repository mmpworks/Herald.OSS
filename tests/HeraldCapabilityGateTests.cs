#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using MMP.Herald;
using MMP.Herald.Pipeline;
using MMP.Herald.Routing;
using Xunit;

namespace MMP.Herald.OSS.Tests;

/// <summary>
/// Pins the gate primitive surface from Echo's test matrix (T-CAP-1..7,
/// T-CAP-24..28, T-CAP-45..48). The gate consults
/// <see cref="HeraldVersion.CurrentCapabilities"/> and
/// <see cref="HeraldVersion.CapabilityResolver"/>; both are process-global
/// mutable state. Each test resets via <c>ResetForTesting</c> before
/// running and on dispose to keep the cap-set isolated across the class
/// (xUnit serializes inside a fixture instance).
/// </summary>
// Touches process-global CurrentCapabilities / CapabilityResolver. xUnit serializes
// within the class, but other collections run in parallel and can mutate the same
// global mid-test — the source of an intermittent failure under parallel runs. Pin to
// a non-parallel collection so it never races another test touching the cap globals.
[Collection(nameof(HeraldCapabilityGateTests))]
[CollectionDefinition(nameof(HeraldCapabilityGateTests), DisableParallelization = true)]
public sealed class HeraldCapabilityGateTests : IDisposable
{
    public HeraldCapabilityGateTests()
    {
        HeraldVersion.ResetForTesting();
        HeraldCapabilityGate.ResetForTesting();
    }

    public void Dispose()
    {
        HeraldVersion.ResetForTesting();
        HeraldCapabilityGate.ResetForTesting();
    }

    private static IReadOnlySet<string> CapSet(params string[] caps) =>
        caps.ToImmutableHashSet(StringComparer.Ordinal);

    // ── T-CAP-1: Require throws on missing cap ──────────────────────

    [Fact]
    public void Require_throws_when_cap_missing()
    {
        Action act = () => HeraldCapabilityGate.Require("multi-tenant", "MyFeature");

        act.Should().Throw<HeraldCapabilityRequirementException>()
            .Which.Capability.Should().Be("multi-tenant");
    }

    [Fact]
    public void Require_exception_names_the_feature_and_tenant_null()
    {
        Action act = () => HeraldCapabilityGate.Require("multi-tenant", "MyFeature");

        var ex = act.Should().Throw<HeraldCapabilityRequirementException>().Subject.Single();
        ex.Feature.Should().Be("MyFeature");
        ex.TenantId.Should().BeNull();
    }

    // ── T-CAP-2: Require succeeds when cap present ──────────────────

    [Fact]
    public void Require_succeeds_when_cap_present()
    {
        HeraldVersion.SetCurrentCapabilities(CapSet("multi-tenant"));

        Action act = () => HeraldCapabilityGate.Require("multi-tenant", "MyFeature");

        act.Should().NotThrow();
    }

    // ── T-CAP-3: AllowsFeature returns bool, never throws ───────────

    [Fact]
    public void AllowsFeature_returns_false_when_missing()
    {
        HeraldCapabilityGate.AllowsFeature("multi-tenant").Should().BeFalse();
    }

    [Fact]
    public void AllowsFeature_returns_true_when_present()
    {
        HeraldVersion.SetCurrentCapabilities(CapSet("multi-tenant"));

        HeraldCapabilityGate.AllowsFeature("multi-tenant").Should().BeTrue();
    }

    // ── T-CAP-4: Exception shape carries actionable message ─────────

    [Fact]
    public void Exception_message_names_capability_feature_and_remediation_hint()
    {
        var ex = new HeraldCapabilityRequirementException("multi-tenant", "MyFeature");

        ex.Capability.Should().Be("multi-tenant");
        ex.Feature.Should().Be("MyFeature");
        ex.TenantId.Should().BeNull();
        // Operator grep target: the "upgrade" remediation phrase from the
        // rev 3 spec must survive any future refactor. Tests T-CAP-4 names
        // this as the actionable hint operators paste into tickets.
        ex.Message.Should().Contain("MyFeature");
        ex.Message.Should().Contain("multi-tenant");
        ex.Message.Should().Contain("Upgrade to a plan that includes");
    }

    [Fact]
    public void Exception_message_names_tenant_when_supplied()
    {
        var ex = new HeraldCapabilityRequirementException(
            "multi-tenant", "MyFeature", tenantId: "acme");

        ex.TenantId.Should().Be("acme");
        ex.Message.Should().Contain("tenant: acme");
    }

    // ── T-CAP-21: Fail-fast on first missing cap (deterministic) ────

    [Fact]
    public void RequireOn_component_throws_on_first_missing_cap_in_declaration_order()
    {
        // Both caps missing. The exception must name the FIRST in
        // declaration order (multi-tenant) — not lexicographically first.
        var component = new FakeComponent("MyComponent",
            ["multi-tenant", "advanced-strategies"]);

        Action act = () => HeraldCapabilityGate.RequireOn(component);

        var ex = act.Should().Throw<HeraldCapabilityRequirementException>().Subject.Single();
        ex.Capability.Should().Be("multi-tenant");
        ex.Feature.Should().Be("MyComponent");
    }

    [Fact]
    public void RequireOn_component_message_is_byte_identical_across_runs()
    {
        var component = new FakeComponent("MyComponent",
            ["multi-tenant", "advanced-strategies"]);

        string? first = null;
        string? second = null;
        try { HeraldCapabilityGate.RequireOn(component); }
        catch (HeraldCapabilityRequirementException ex) { first = ex.Message; }
        try { HeraldCapabilityGate.RequireOn(component); }
        catch (HeraldCapabilityRequirementException ex) { second = ex.Message; }

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        second.Should().Be(first);
    }

    [Fact]
    public void RequireOn_component_with_no_caps_does_not_throw()
    {
        var component = new FakeComponent("NoCapComponent", []);

        Action act = () => HeraldCapabilityGate.RequireOn(component);

        act.Should().NotThrow();
    }

    [Fact]
    public void RequireOn_component_with_all_caps_granted_does_not_throw()
    {
        HeraldVersion.SetCurrentCapabilities(CapSet("a", "b", "c"));
        var component = new FakeComponent("Comp", ["a", "b"]);

        Action act = () => HeraldCapabilityGate.RequireOn(component);

        act.Should().NotThrow();
    }

    // ── T-CAP-24/25: Per-tenant resolver consultation + default ─────

    [Fact]
    public void RequireFor_consults_per_tenant_resolver()
    {
        // tenant-a has the cap; tenant-b does not.
        HeraldVersion.CapabilityResolver = tenantId =>
            tenantId == "tenant-a" ? CapSet("compliance-overlay") : CapSet();

        Action actA = () => HeraldCapabilityGate.RequireFor("tenant-a", "compliance-overlay", "X");
        Action actB = () => HeraldCapabilityGate.RequireFor("tenant-b", "compliance-overlay", "X");

        actA.Should().NotThrow();
        actB.Should().Throw<HeraldCapabilityRequirementException>()
            .Which.TenantId.Should().Be("tenant-b");
    }

    [Fact]
    public void Default_resolver_returns_process_global_caps_for_null_and_named_tenants()
    {
        HeraldVersion.SetCurrentCapabilities(CapSet("multi-tenant"));

        HeraldVersion.CapabilityResolver(null).Should().Contain("multi-tenant");
        HeraldVersion.CapabilityResolver("any-tenant").Should().Contain("multi-tenant");
    }

    [Fact]
    public void Assigning_null_resolver_restores_default()
    {
        HeraldVersion.SetCurrentCapabilities(CapSet("multi-tenant"));
        HeraldVersion.CapabilityResolver = _ => CapSet();   // hide caps
        HeraldVersion.CapabilityResolver.Invoke(null).Should().BeEmpty();

        HeraldVersion.CapabilityResolver = null!;            // restore default
        HeraldVersion.CapabilityResolver(null).Should().Contain("multi-tenant");
    }

    // ── T-CAP-26: OnCapabilityChecked fires on both paths ───────────

    [Fact]
    public void OnCapabilityChecked_fires_on_success_path()
    {
        HeraldVersion.SetCurrentCapabilities(CapSet("multi-tenant"));
        var events = new List<CapabilityGateEvaluation>();
        HeraldCapabilityGate.OnCapabilityChecked += events.Add;

        HeraldCapabilityGate.Require("multi-tenant", "MyFeature");

        events.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Capability = "multi-tenant",
                FeatureName = "MyFeature",
                TenantId = (string?)null,
                Granted = true,
            }, options => options.ExcludingMissingMembers());
    }

    [Fact]
    public void OnCapabilityChecked_fires_on_failure_path()
    {
        var events = new List<CapabilityGateEvaluation>();
        HeraldCapabilityGate.OnCapabilityChecked += events.Add;

        try { HeraldCapabilityGate.Require("multi-tenant", "MyFeature"); }
        catch (HeraldCapabilityRequirementException) { /* expected */ }

        events.Should().ContainSingle()
            .Which.Granted.Should().BeFalse();
    }

    // ── T-CAP-27: handler-throwing is isolated ──────────────────────

    [Fact]
    public void Throwing_subscriber_does_not_corrupt_gate_or_other_subscribers()
    {
        HeraldVersion.SetCurrentCapabilities(CapSet("multi-tenant"));
        var counter = 0;
        HeraldCapabilityGate.OnCapabilityChecked += _ => throw new InvalidOperationException("boom");
        HeraldCapabilityGate.OnCapabilityChecked += _ => counter++;

        Action act = () => HeraldCapabilityGate.Require("multi-tenant", "MyFeature");

        act.Should().NotThrow();
        counter.Should().Be(1, "the counting subscriber must still receive every event");

        // Second call still fires the counter — the throwing subscriber
        // didn't leave the gate in a stuck state.
        HeraldCapabilityGate.Require("multi-tenant", "MyFeature");
        counter.Should().Be(2);
    }

    // ── T-CAP-47: TenantId propagates through RequireFor ────────────

    [Fact]
    public void TenantId_propagates_into_event_payload_through_RequireFor()
    {
        // Both Require and RequireFor must see the cap; Require reads from
        // CurrentCapabilities directly, RequireFor reads through the
        // resolver. Seed both so the two calls succeed and the test can
        // assert on the TenantId field shape (the actual observation).
        HeraldVersion.SetCurrentCapabilities(CapSet("multi-tenant"));
        HeraldVersion.CapabilityResolver = _ => CapSet("multi-tenant");
        var captured = new List<CapabilityGateEvaluation>();
        HeraldCapabilityGate.OnCapabilityChecked += captured.Add;

        HeraldCapabilityGate.RequireFor("tenant-a", "multi-tenant", "MyFeature");
        HeraldCapabilityGate.Require("multi-tenant", "MyFeature");

        captured.Should().HaveCount(2);
        captured[0].TenantId.Should().Be("tenant-a");
        captured[1].TenantId.Should().BeNull();
    }

    // ── T-CAP-48: Multiple subscribers each see every event ─────────

    [Fact]
    public void Two_subscribers_each_receive_every_event()
    {
        var a = 0;
        var b = 0;
        HeraldCapabilityGate.OnCapabilityChecked += _ => a++;
        HeraldCapabilityGate.OnCapabilityChecked += _ => b++;

        try { HeraldCapabilityGate.Require("nope", "X"); }
        catch (HeraldCapabilityRequirementException) { /* expected */ }

        a.Should().Be(1);
        b.Should().Be(1);
    }

    // ── T-CAP-45: Per-tenant resolver disjoint sets ─────────────────

    [Fact]
    public void Disjoint_per_tenant_resolver_correctly_isolates_two_tenants()
    {
        HeraldVersion.CapabilityResolver = tenantId => tenantId switch
        {
            "tenant-a" => CapSet("multi-tenant", "compliance-overlay"),
            "tenant-b" => CapSet("advanced-strategies"),
            _ => CapSet(),
        };

        Action aMt = () => HeraldCapabilityGate.RequireFor("tenant-a", "multi-tenant", "X");
        Action bMt = () => HeraldCapabilityGate.RequireFor("tenant-b", "multi-tenant", "X");
        Action aAs = () => HeraldCapabilityGate.RequireFor("tenant-a", "advanced-strategies", "X");
        Action bAs = () => HeraldCapabilityGate.RequireFor("tenant-b", "advanced-strategies", "X");

        aMt.Should().NotThrow();
        bMt.Should().Throw<HeraldCapabilityRequirementException>();
        aAs.Should().Throw<HeraldCapabilityRequirementException>();
        bAs.Should().NotThrow();
    }

    // ── T-CAP-46: null tenant id falls back to process-global ───────

    [Fact]
    public void RequireFor_with_null_tenant_id_consults_the_resolver_default_branch()
    {
        HeraldVersion.SetCurrentCapabilities(CapSet("process-global-cap"));
        HeraldVersion.CapabilityResolver = tenantId =>
            tenantId is null ? HeraldVersion.CurrentCapabilities : CapSet("tenant-cap");

        Action act = () => HeraldCapabilityGate.RequireFor(null, "process-global-cap", "X");

        act.Should().NotThrow();
    }

    // ── T-CAP-5: Cap path wins over edition path (sibling pattern) ──

    [Fact]
    public void Cap_path_throws_capability_exception_not_edition_exception()
    {
        // Cap path is consulted via RequireOn(component); the edition
        // gate is a sibling, not a fallback. The component declaring caps
        // gets the capability-shaped exception even though Community
        // edition would also fail the edition gate.
        HeraldVersion.SetEdition(HeraldEdition.Community);   // explicit
        var component = new FakeComponent("EnterpriseSink",
            ["multi-tenant"], minimumEdition: HeraldEdition.Enterprise);

        Action act = () => HeraldCapabilityGate.RequireOn(component);

        act.Should().Throw<HeraldCapabilityRequirementException>();
        act.Should().NotThrow<HeraldEditionRequirementException>();
    }

    // ── Internal RequireOn(IReadOnlySet<string>) overload (test seam) ──

    [Fact]
    public void Internal_RequireOn_with_explicit_cap_set_drives_the_same_logic()
    {
        var caps = CapSet("multi-tenant");

        Action ok = () => HeraldCapabilityGate.RequireOn(caps, "multi-tenant", "X");
        Action bad = () => HeraldCapabilityGate.RequireOn(caps, "missing-cap", "X");

        ok.Should().NotThrow();
        bad.Should().Throw<HeraldCapabilityRequirementException>();
    }

    /// <summary>Test double implementing <see cref="IComponentMetadata"/>.</summary>
    private sealed class FakeComponent : IComponentMetadata
    {
        public FakeComponent(
            string name,
            IReadOnlyList<CapabilityRequirement> requiredCapabilities,
            HeraldEdition? minimumEdition = null)
        {
            ComponentName = name;
            RequiredCapabilities = requiredCapabilities;
            MinimumEdition = minimumEdition ?? HeraldEdition.Community;
        }

        public string ComponentName { get; }
        public string DisplayName => ComponentName;
        public string Description => "";
        public string Help => "";
        public VendorInfo Vendor => VendorInfo.MMP;
        public IReadOnlyList<SinkConfigField> ConfigurationSchema => [];
        public HeraldEdition MinimumEdition { get; }
        public IReadOnlyList<CapabilityRequirement> RequiredCapabilities { get; }
    }
}
