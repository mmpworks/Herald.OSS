#nullable enable

using System;
using FluentAssertions;
using MMP.Herald;
using MMP.Herald.Configuration;
using Xunit;

namespace MMP.Herald.OSS.Tests.PipelineStrategyTests;

/// <summary>
/// Pins the cap-surface additions on <see cref="PipelineStep.Register"/>:
/// new <c>requiredCapabilities</c> parameter (T-CAP-7 portion), cap
/// monotonicity rule on re-registration (T-CAP-22), and the carve-out
/// for the legacy-edition-only-to-caps transition (T-CAP-23).
///
/// <para>
/// PipelineStep uses a process-global registry (ConcurrentDictionary).
/// Tests register under unique names to avoid cross-contamination — the
/// registry has no reset hook by design (plugin module-initializers
/// shouldn't deregister at runtime). Each test uses a guaranteed-unique
/// step name derived from the test method name.
/// </para>
/// </summary>
public sealed class PipelineStepCapabilityTests
{
    [Fact]
    public void Register_with_no_caps_adds_step_with_empty_RequiredCapabilities()
    {
        var step = PipelineStep.Register("psct-no-caps", "Test", "");

        step.RequiredCapabilities.Should().BeEmpty();
    }

    [Fact]
    public void Register_with_caps_records_the_declared_set()
    {
        var step = PipelineStep.Register("psct-with-caps", "Test", "",
            requiredCapabilities: ["multi-tenant", "compliance-overlay"]);

        step.RequiredCapabilities.Should().HaveCount(2);
        step.RequiredCapabilities[0].Name.Should().Be("multi-tenant");
        step.RequiredCapabilities[1].Name.Should().Be("compliance-overlay");
    }

    // ── T-CAP-22: cap monotonicity (re-register with subset throws) ──

    [Fact]
    public void Re_register_with_strict_subset_of_caps_throws()
    {
        PipelineStep.Register("psct-monotonic",
            requiredCapabilities: ["multi-tenant", "compliance-overlay"]);

        Action act = () => PipelineStep.Register("psct-monotonic",
            requiredCapabilities: ["multi-tenant"]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*compliance-overlay*");
    }

    [Fact]
    public void Re_register_with_superset_of_caps_succeeds()
    {
        PipelineStep.Register("psct-superset",
            requiredCapabilities: ["multi-tenant"]);

        var step = PipelineStep.Register("psct-superset",
            requiredCapabilities: ["multi-tenant", "advanced-strategies"]);

        step.RequiredCapabilities.Should().HaveCount(2);
    }

    [Fact]
    public void Re_register_with_same_cap_set_succeeds()
    {
        PipelineStep.Register("psct-same",
            requiredCapabilities: ["multi-tenant"]);

        Action act = () => PipelineStep.Register("psct-same",
            requiredCapabilities: ["multi-tenant"]);

        act.Should().NotThrow();
    }

    // ── T-CAP-23: cap monotonicity carve-out (edition-only prior) ────

    [Fact]
    public void Re_register_from_edition_only_to_cap_declared_succeeds()
    {
        // First registration is edition-only (no caps). The carve-out
        // says any new cap-set is accepted — without it, every legacy
        // step would be permanently barred from adopting caps.
        PipelineStep.Register("psct-carveout",
            minimumEdition: HeraldEdition.Pro);

        Action act = () => PipelineStep.Register("psct-carveout",
            requiredCapabilities: ["multi-tenant"]);

        act.Should().NotThrow();
        PipelineStep.FromName("psct-carveout")!.RequiredCapabilities
            .Should().Contain(r => r.Name == "multi-tenant");
    }

    [Fact]
    public void Re_register_does_not_clear_prior_caps_when_no_caps_argument_passed()
    {
        PipelineStep.Register("psct-no-clobber",
            requiredCapabilities: ["multi-tenant"]);

        // Re-register with metadata changes but no caps argument; the
        // prior caps must persist.
        PipelineStep.Register("psct-no-clobber", "Updated Display");

        var step = PipelineStep.FromName("psct-no-clobber");
        step!.RequiredCapabilities.Should().HaveCount(1);
        step.RequiredCapabilities[0].Name.Should().Be("multi-tenant");
    }
}
