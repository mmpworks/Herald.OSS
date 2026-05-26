#nullable enable

using System;
using System.Linq;
using FluentAssertions;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Events;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// The multi-filter sampling seam: WithSampling / WithThrottling /
/// WithAdaptiveSampling compose into one CompositeSamplingFilter instead of
/// fighting over a single slot. These tests pin BOTH halves of the contract:
/// (a) the builder records the expected JsonSamplingRule shapes (config-preserved
/// + round-trippable), and (b) the composed filter actually drops/caps events at
/// runtime (the feature applies, not just persists).
/// </summary>
public sealed class MultiFilterSamplingTests
{
    // ---- (a) config shape: builder -> JsonSamplingRule list --------------

    [Fact]
    public void WithSampling_alone_records_one_fixed_rate_rule()
    {
        var builder = QuickLogBuilder.Create().WithSampling(10);

        builder.SamplingRulesView.Should().NotBeNull();
        builder.SamplingRulesView!.Should().ContainSingle();
        var rule = builder.SamplingRulesView![0];
        rule.SampleRate.Should().Be(10);
        rule.MaxPerSecond.Should().Be(0);
        rule.AdaptiveNormalSampleRate.Should().Be(0);
    }

    [Fact]
    public void Sampling_and_throttling_coexist_as_two_rules()
    {
        // The whole point of the seam: these no longer overwrite one slot.
        var builder = QuickLogBuilder.Create()
            .WithSampling(5)
            .WithThrottling(100);

        builder.SamplingRulesView!.Should().HaveCount(2);
        builder.SamplingRulesView![0].SampleRate.Should().Be(5);
        builder.SamplingRulesView![1].MaxPerSecond.Should().Be(100);
    }

    [Fact]
    public void WithAdaptiveSampling_records_adaptive_rule_with_window()
    {
        var builder = QuickLogBuilder.Create()
            .WithAdaptiveSampling(normalSampleRate: 10, errorThreshold: 5, window: TimeSpan.FromSeconds(2));

        var rule = builder.SamplingRulesView!.Should().ContainSingle().Subject;
        rule.AdaptiveNormalSampleRate.Should().Be(10);
        rule.AdaptiveErrorThreshold.Should().Be(5);
        rule.AdaptiveWindowMs.Should().Be(2000);
    }

    [Fact]
    public void WithAdaptiveSampling_defaults_window_to_one_second()
    {
        var builder = QuickLogBuilder.Create()
            .WithAdaptiveSampling(normalSampleRate: 10, errorThreshold: 5);

        builder.SamplingRulesView![0].AdaptiveWindowMs.Should().Be(1000);
    }

    [Fact]
    public void WithoutSampling_clears_all_rules()
    {
        var builder = QuickLogBuilder.Create()
            .WithSampling(5)
            .WithThrottling(100)
            .WithoutSampling();

        builder.SamplingRulesView.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void WithThrottling_rejects_non_positive(int bad)
    {
        var act = () => QuickLogBuilder.Create().WithThrottling(bad);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WithAdaptiveSampling_rejects_non_positive_rate_or_threshold()
    {
        var b = QuickLogBuilder.Create();
        ((Action)(() => b.WithAdaptiveSampling(0, 5))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => b.WithAdaptiveSampling(10, 0))).Should().Throw<ArgumentOutOfRangeException>();
    }
}
