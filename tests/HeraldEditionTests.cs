#nullable enable

using FluentAssertions;
using MMP.Herald;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Routing;
using Xunit;

namespace MMP.Herald.OSS.Tests;

/// <summary>
/// Pins the shape of the informational <see cref="HeraldEdition"/> type
/// and its default surface on <see cref="ILogSinkProvider"/>. Herald.OSS
/// enforces nothing against MinimumEdition — these tests pin the
/// metadata contract so a downstream commercial wrapper can rely on the
/// shape without reading source.
/// </summary>
public sealed class HeraldEditionTests
{
    [Fact]
    public void Three_canonical_editions_have_ascending_rank()
    {
        HeraldEdition.Community.Rank.Should().Be(0);
        HeraldEdition.Pro.Rank.Should().Be(1);
        HeraldEdition.Enterprise.Rank.Should().Be(2);
    }

    [Fact]
    public void HeraldEdition_Dev_Exists_WithRankMinusOne()
    {
        HeraldEdition.Dev.Should().NotBeNull();
        HeraldEdition.Dev.Name.Should().Be("Dev");
        HeraldEdition.Dev.Rank.Should().Be(-1);
    }

    [Fact]
    public void Includes_returns_true_when_current_tier_meets_or_exceeds_required()
    {
        HeraldEdition.Enterprise.Includes(HeraldEdition.Community).Should().BeTrue();
        HeraldEdition.Enterprise.Includes(HeraldEdition.Pro).Should().BeTrue();
        HeraldEdition.Enterprise.Includes(HeraldEdition.Enterprise).Should().BeTrue();

        HeraldEdition.Pro.Includes(HeraldEdition.Community).Should().BeTrue();
        HeraldEdition.Pro.Includes(HeraldEdition.Pro).Should().BeTrue();

        HeraldEdition.Community.Includes(HeraldEdition.Community).Should().BeTrue();
    }

    [Fact]
    public void Includes_returns_false_when_current_tier_is_below_required()
    {
        HeraldEdition.Community.Includes(HeraldEdition.Pro).Should().BeFalse();
        HeraldEdition.Community.Includes(HeraldEdition.Enterprise).Should().BeFalse();
        HeraldEdition.Pro.Includes(HeraldEdition.Enterprise).Should().BeFalse();
    }

    [Fact]
    public void ToString_renders_the_edition_name()
    {
        HeraldEdition.Community.ToString().Should().Be("Community");
        HeraldEdition.Pro.ToString().Should().Be("Pro");
        HeraldEdition.Enterprise.ToString().Should().Be("Enterprise");
    }

    [Fact]
    public void Record_equality_is_value_based()
    {
        var customCommunity = new HeraldEdition("Community", 0);
        customCommunity.Should().Be(HeraldEdition.Community);
    }

    [Fact]
    public void Default_minimum_edition_on_sink_provider_is_community()
    {
        // A sink provider that does NOT override MinimumEdition must
        // surface Community via the interface default. This is the seam
        // a downstream commercial wrapper relies on: any sink that doesn't
        // declare itself paid is treated as Community-tier.
        ILogSinkProvider provider = new MinimalSinkProvider();
        provider.MinimumEdition.Should().BeSameAs(HeraldEdition.Community,
            "the interface default must surface Community for sinks that don't override");
    }

    [Fact]
    public void Sink_provider_can_override_minimum_edition()
    {
        // Pin the override path so a downstream Pro/Enterprise-only sink
        // can declare itself as such without OSS interfering.
        ILogSinkProvider provider = new EnterpriseOnlySinkProvider();
        provider.MinimumEdition.Should().BeSameAs(HeraldEdition.Enterprise);
    }

    private sealed class MinimalSinkProvider : ILogSinkProvider
    {
        public string SinkKind => "minimal-test-sink";
        public ILogger CreateSink(
            LoggingRuntimeSinkDefinition definition,
            ILogLevelRegistry levelRegistry,
            ILogOutputTransformerRegistry transformerRegistry) =>
            new NoopLogger();
    }

    private sealed class EnterpriseOnlySinkProvider : ILogSinkProvider
    {
        public string SinkKind => "enterprise-only-test-sink";
        public HeraldEdition MinimumEdition => HeraldEdition.Enterprise;
        public ILogger CreateSink(
            LoggingRuntimeSinkDefinition definition,
            ILogLevelRegistry levelRegistry,
            ILogOutputTransformerRegistry transformerRegistry) =>
            new NoopLogger();
    }

    private sealed class NoopLogger : ILogger
    {
        public void Log(LogEvent logEvent) { }
        public System.Threading.Tasks.ValueTask LogAsync(LogEvent logEvent, System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.ValueTask.CompletedTask;
    }
}
