#nullable enable

using System;
using FluentAssertions;
using MMP.Herald;
using MMP.Herald.Configuration;
using Xunit;

namespace MMP.Herald.Tests.FlightRecorderCommunityShapeNS;

/// <summary>
/// Pins the Community-tier Flight Recorder lock: bufferSize 200, no
/// minimum-level override, null-or-"error" trigger. Anything else requires
/// Pro and routes through <see cref="HeraldEditionGate"/>.
/// </summary>
public sealed class FlightRecorderCommunityShapeTests
{
    // ── Matches() — the predicate side ──────────────────────────────

    [Fact]
    public void Canonical_shape_matches_community_lock()
    {
        FlightRecorderCommunityShape.Matches(200, null, null).Should().BeTrue();
        FlightRecorderCommunityShape.Matches(200, null, "error").Should().BeTrue();
        FlightRecorderCommunityShape.Matches(200, null, "ERROR").Should().BeTrue();
        FlightRecorderCommunityShape.Matches(200, "", "").Should().BeTrue();
    }

    [Theory]
    [InlineData(201, null, null)]
    [InlineData(100, null, null)]
    [InlineData(1000, null, null)]
    [InlineData(200, "trace", null)]
    [InlineData(200, "debug", "error")]
    [InlineData(200, null, "warn")]
    [InlineData(200, null, "fatal")]
    [InlineData(500, "trace", "warn")]
    public void Non_canonical_shape_does_not_match(int bufferSize, string? minLevel, string? triggerLevel)
    {
        FlightRecorderCommunityShape.Matches(bufferSize, minLevel, triggerLevel).Should().BeFalse();
    }

    // ── RequireConfigurabilityEditionOn() — the gate side ───────────

    [Fact]
    public void Community_edition_accepts_the_canonical_shape()
    {
        var act = () => FlightRecorderCommunityShape.RequireConfigurabilityEditionOn(
            HeraldEdition.Community, 200, null, null);
        act.Should().NotThrow();
    }

    [Fact]
    public void Community_edition_accepts_null_levels_with_explicit_error_trigger()
    {
        var act = () => FlightRecorderCommunityShape.RequireConfigurabilityEditionOn(
            HeraldEdition.Community, 200, null, "error");
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(500, null, null)]
    [InlineData(200, "trace", null)]
    [InlineData(200, null, "warn")]
    public void Community_edition_rejects_any_deviation(int bufferSize, string? minLevel, string? triggerLevel)
    {
        var act = () => FlightRecorderCommunityShape.RequireConfigurabilityEditionOn(
            HeraldEdition.Community, bufferSize, minLevel, triggerLevel);

        act.Should().Throw<HeraldEditionRequirementException>()
            .WithMessage("*Flight Recorder configurability*")
            .WithMessage("*requires the Pro edition*");
    }

    [Fact]
    public void Pro_edition_unlocks_arbitrary_configuration()
    {
        var act = () => FlightRecorderCommunityShape.RequireConfigurabilityEditionOn(
            HeraldEdition.Pro, 5000, "trace", "warn");
        act.Should().NotThrow();
    }

    [Fact]
    public void Enterprise_edition_unlocks_arbitrary_configuration()
    {
        var act = () => FlightRecorderCommunityShape.RequireConfigurabilityEditionOn(
            HeraldEdition.Enterprise, 50_000, "debug", "fatal");
        act.Should().NotThrow();
    }
}
