#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FluentAssertions;
using MMP.Herald;
using Xunit;

namespace MMP.Herald.OSS.Tests;

/// <summary>
/// Pins the install-hook + replace semantics on
/// <see cref="HeraldVersion.CurrentCapabilities"/> and
/// <see cref="HeraldVersion.CapabilityResolver"/>. Mirrors the existing
/// <see cref="HeraldVersionEditionTests"/> shape: process-global mutable
/// state, reset before and after each test.
/// </summary>
public sealed class HeraldVersionCapabilitiesTests : IDisposable
{
    public HeraldVersionCapabilitiesTests()
    {
        HeraldVersion.ResetForTesting();
    }

    public void Dispose()
    {
        HeraldVersion.ResetForTesting();
    }

    private static IReadOnlySet<string> CapSet(params string[] caps) =>
        ImmutableHashSet.Create(StringComparer.Ordinal, caps);

    // ── CurrentCapabilities default ─────────────────────────────────

    [Fact]
    public void CurrentCapabilities_defaults_to_empty()
    {
        HeraldVersion.CurrentCapabilities.Should().BeEmpty();
    }

    // ── SetCurrentCapabilities: first-write-wins ────────────────────

    [Fact]
    public void SetCurrentCapabilities_first_call_wins_subsequent_calls_noop()
    {
        var first = CapSet("multi-tenant");
        var second = CapSet("advanced-strategies");

        HeraldVersion.SetCurrentCapabilities(first);
        HeraldVersion.SetCurrentCapabilities(second);

        HeraldVersion.CurrentCapabilities.Should().BeSameAs(first);
    }

    [Fact]
    public void SetCurrentCapabilities_null_throws()
    {
        Action act = () => HeraldVersion.SetCurrentCapabilities(null!);
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("capabilities");
    }

    // ── ReplaceCurrentCapabilities: unconditional swap (G-5 hook) ───

    [Fact]
    public void ReplaceCurrentCapabilities_swaps_unconditionally()
    {
        var initial = CapSet("multi-tenant");
        var updated = CapSet("multi-tenant", "compliance-overlay");

        HeraldVersion.SetCurrentCapabilities(initial);
        HeraldVersion.ReplaceCurrentCapabilities(updated);

        HeraldVersion.CurrentCapabilities.Should().BeSameAs(updated);
        HeraldVersion.CurrentCapabilities.Should().Contain("compliance-overlay");
    }

    [Fact]
    public void ReplaceCurrentCapabilities_can_narrow_the_set()
    {
        // G-5 license state machine: a check-in response narrowing the
        // cap-set (e.g. customer dropped from Enterprise to Pro) must
        // take effect on the next gate evaluation.
        HeraldVersion.SetCurrentCapabilities(CapSet("a", "b", "c"));
        HeraldVersion.ReplaceCurrentCapabilities(CapSet("a"));

        HeraldVersion.CurrentCapabilities.Should().HaveCount(1);
        HeraldVersion.CurrentCapabilities.Should().Contain("a");
    }

    [Fact]
    public void ReplaceCurrentCapabilities_null_throws()
    {
        Action act = () => HeraldVersion.ReplaceCurrentCapabilities(null!);
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("capabilities");
    }

    // ── CapabilityResolver default + assignment ─────────────────────

    [Fact]
    public void CapabilityResolver_default_returns_process_global_for_every_tenant()
    {
        HeraldVersion.SetCurrentCapabilities(CapSet("multi-tenant"));

        HeraldVersion.CapabilityResolver(null).Should().BeSameAs(HeraldVersion.CurrentCapabilities);
        HeraldVersion.CapabilityResolver("acme").Should().BeSameAs(HeraldVersion.CurrentCapabilities);
    }

    [Fact]
    public void CapabilityResolver_assignment_replaces_default()
    {
        var customSet = CapSet("custom-cap");
        HeraldVersion.CapabilityResolver = _ => customSet;

        HeraldVersion.CapabilityResolver(null).Should().BeSameAs(customSet);
    }

    [Fact]
    public void CapabilityResolver_assignment_to_null_restores_default()
    {
        HeraldVersion.SetCurrentCapabilities(CapSet("global"));
        HeraldVersion.CapabilityResolver = _ => CapSet("override");
        HeraldVersion.CapabilityResolver(null).Should().NotBeSameAs(HeraldVersion.CurrentCapabilities);

        HeraldVersion.CapabilityResolver = null!;

        HeraldVersion.CapabilityResolver(null).Should().BeSameAs(HeraldVersion.CurrentCapabilities);
    }

    // ── ResetForTesting clears all cap state ────────────────────────

    [Fact]
    public void ResetForTesting_clears_capabilities_and_restores_default_resolver()
    {
        HeraldVersion.SetCurrentCapabilities(CapSet("a"));
        HeraldVersion.CapabilityResolver = _ => CapSet("b");

        HeraldVersion.ResetForTesting();

        HeraldVersion.CurrentCapabilities.Should().BeEmpty();
        HeraldVersion.CapabilityResolver(null).Should().BeSameAs(HeraldVersion.CurrentCapabilities);
    }
}
