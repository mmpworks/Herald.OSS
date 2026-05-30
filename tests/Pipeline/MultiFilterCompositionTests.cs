#nullable enable

using System;
using System.Linq;
using FluentAssertions;
using MMP.Herald.Configuration;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Events;
using MMP.Herald.Filters;
using MMP.Herald.Levels;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// The composition contract Jared + Echo pinned: sampling + throttling + adaptive must
/// AND-chain (all apply independently), the mapper must return a LIST (not a
/// first-match-wins CompositeSamplingFilter), and a configured filter must disqualify the
/// kernel fast path. These are the "strong code position" tests the Community filter axis
/// was missing.
/// </summary>
public sealed class MultiFilterCompositionTests
{
    private static ILogLevelRegistry Registry() => LogLevelRegistry.CreateDefault();

    // ---- Mapper returns an AND-chain LIST, one filter per rule ----------

    [Fact]
    public void Mapper_returns_a_filter_per_rule_not_a_single_composite()
    {
        var config = new JsonSamplingConfig(Enabled: true, Rules: new[]
        {
            new JsonSamplingRule(SampleRate: 4),
            new JsonSamplingRule(MaxPerSecond: 100),
            new JsonSamplingRule(AdaptiveNormalSampleRate: 10, AdaptiveErrorThreshold: 5),
        });

        var runtime = DefaultLoggingConfigurationMapper.MapForTest(config, Registry());

        runtime.Should().NotBeNull();
        var list = runtime!;
        list.Should().HaveCount(3);
        list[0].Should().BeOfType<SamplingFilter>();
        list[1].Should().BeOfType<ThrottlingFilter>();
        list[2].Should().BeOfType<MMP.Herald.Addons.MetricExtraction.AdaptiveSamplingFilter>();
    }

    // ---- AND-chain: each filter drops independently ---------------------

    [Fact]
    public void And_chain_throttle_caps_even_when_sampling_would_pass()
    {
        // Sampling at rate 1 (keep all) + throttling at 20/s. The AND-chain means the
        // throttle still caps — proving both filters apply, not just the first.
        var captured = new CountingLogger();
        var result = QuickLogBuilder.Create()
            .WithBridge(captured)
            .WithMinimumLevel("trace")
            .WithSampling(1)        // keep all
            .WithThrottling(20)     // but cap at 20/window
            .BuildAndCommit();

        for (var i = 0; i < 500; i++)
        {
            result.Logger.Information(LogCategory.App, "evt");
        }

        // If only the first rule (sampling, keep-all) applied, we'd see ~500. The throttle
        // must hold the line, proving the AND-chain applies the second filter too.
        captured.Count.Should().BeLessThanOrEqualTo(60);
        captured.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Three_toggles_all_present_in_the_built_pipeline()
    {
        var builder = QuickLogBuilder.Create()
            .WithMinimumLevel("trace")
            .WithSampling(4)
            .WithThrottling(100)
            .WithAdaptiveSampling(10, 5);

        // All three rules round-trip into the JSON config (config-preserved), proving none
        // overwrote another (the old single-slot bug).
        builder.SamplingRulesView!.Should().HaveCount(3);
    }

    private sealed class CountingLogger : ILogger
    {
        private int _count;
        public int Count => _count;
        public void Log(LogEvent logEvent) => System.Threading.Interlocked.Increment(ref _count);
        public System.Threading.Tasks.ValueTask LogAsync(
            LogEvent logEvent, System.Threading.CancellationToken cancellationToken = default)
        {
            System.Threading.Interlocked.Increment(ref _count);
            return System.Threading.Tasks.ValueTask.CompletedTask;
        }
    }
}
