#nullable enable

using System.Linq;
using FluentAssertions;
using MMP.Herald.Configuration;
using Xunit;

namespace MMP.Herald.OSS.Tests.PipelineStrategyTests;

/// <summary>
/// Pins the principal-review #20 validator-collapse fix:
/// <see cref="PipelineStrategy.ValidateDetailed"/> and
/// <see cref="PipelineStrategy.ValidateStepNames"/> share one rule walker
/// so duplicate-detection, rule wording, severities, and AffectedSteps
/// stay synchronised. Prior to the collapse the two entry points emitted
/// slightly different IncompatibleWith messages — locked here to one
/// shape.
/// </summary>
public sealed class PipelineStrategyValidatorTests
{
    [Fact]
    public void ValidateDetailed_reports_duplicate_step()
    {
        // Two Filterings in the chain — duplicate rule must fire on both
        // entry points with the same wording.
        var strategy = PipelineStrategy.Create()
            .Swappable()
            .Filtering()
            .Filtering()
            .FanOut();

        var issues = strategy.ValidateDetailed();
        var duplicate = issues.Where(i =>
            i.Severity == "error" && i.Message.Contains("Duplicate step")).ToList();

        duplicate.Should().NotBeEmpty();
        duplicate[0].Message.Should().Contain("Each step should appear at most once.");
    }

    [Fact]
    public void ValidateStepNames_reports_duplicate_step_with_same_wording()
    {
        var detailedIssues = PipelineStrategy.Create()
            .Swappable()
            .Filtering()
            .Filtering()
            .FanOut()
            .ValidateDetailed();

        var stepNameIssues = PipelineStrategy.ValidateStepNames(
            new[] { "swappable", "filtering", "filtering", "fanOut" });

        var detailedDup = detailedIssues.First(i => i.Message.Contains("Duplicate step"));
        var stepNameDup = stepNameIssues.First(i => i.Message.Contains("Duplicate step"));

        // Same exact wording — the locked-shape invariant the collapse ensures.
        stepNameDup.Message.Should().Be(detailedDup.Message);
        stepNameDup.Severity.Should().Be(detailedDup.Severity);
    }

    [Fact]
    public void ValidateStepNames_skips_unknown_names_silently()
    {
        // Same forgiveness the pre-refactor implementation had: FromName
        // returns null for unknown names and the loop skips them. Keeps the
        // dashboard's local-arrangement validation usable when a plugin
        // step isn't loaded.
        var issues = PipelineStrategy.ValidateStepNames(
            new[] { "swappable", "totally-unknown-step", "fanOut" });

        issues.Should().BeEmpty(
            "unknown step names are skipped; only known steps participate in rule evaluation");
    }

    [Fact]
    public void Default_preset_passes_validation_without_errors()
    {
        // Sanity check: the built-in Default() preset must not produce its
        // own validation errors after the collapse.
        var issues = PipelineStrategy.Default().ValidateDetailed();

        issues.Where(i => i.Severity == "error").Should().BeEmpty();
    }
}
