#nullable enable

using FluentAssertions;
using MMP.Herald.Diagnostics;
using Xunit;

namespace MMP.Herald.OSS.Tests.Diagnostics;

/// <summary>
/// Contract tests for <see cref="NoticeSeverity"/>. Same shape as
/// <c>HeraldEditionTests</c> — pin the rank ordering, the Includes
/// semantics, and the ToString surface so a future tier addition
/// (or rename) shows up as a test failure.
/// </summary>
public sealed class NoticeSeverityTests
{
    [Fact]
    public void Three_canonical_severities_have_ascending_rank()
    {
        NoticeSeverity.Info.Rank.Should().Be(0);
        NoticeSeverity.Warning.Rank.Should().Be(1);
        NoticeSeverity.Error.Rank.Should().Be(2);
    }

    [Fact]
    public void Includes_returns_true_when_current_meets_or_exceeds_required()
    {
        NoticeSeverity.Error.Includes(NoticeSeverity.Info).Should().BeTrue();
        NoticeSeverity.Error.Includes(NoticeSeverity.Warning).Should().BeTrue();
        NoticeSeverity.Error.Includes(NoticeSeverity.Error).Should().BeTrue();

        NoticeSeverity.Warning.Includes(NoticeSeverity.Info).Should().BeTrue();
        NoticeSeverity.Warning.Includes(NoticeSeverity.Warning).Should().BeTrue();

        NoticeSeverity.Info.Includes(NoticeSeverity.Info).Should().BeTrue();
    }

    [Fact]
    public void Includes_returns_false_when_current_is_below_required()
    {
        NoticeSeverity.Info.Includes(NoticeSeverity.Warning).Should().BeFalse();
        NoticeSeverity.Info.Includes(NoticeSeverity.Error).Should().BeFalse();
        NoticeSeverity.Warning.Includes(NoticeSeverity.Error).Should().BeFalse();
    }

    [Fact]
    public void ToString_renders_the_severity_name()
    {
        NoticeSeverity.Info.ToString().Should().Be("Info");
        NoticeSeverity.Warning.ToString().Should().Be("Warning");
        NoticeSeverity.Error.ToString().Should().Be("Error");
    }

    [Fact]
    public void Record_equality_is_value_based()
    {
        var customInfo = new NoticeSeverity("Info", 0);
        customInfo.Should().Be(NoticeSeverity.Info);
    }
}
