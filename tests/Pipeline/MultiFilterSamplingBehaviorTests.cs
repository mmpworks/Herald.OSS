#nullable enable

using System.Collections.Concurrent;
using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// Behavioral half of the multi-filter seam: the composed CompositeSamplingFilter
/// must actually drop/cap events at runtime, not merely persist config. These build
/// a real pipeline through QuickLogBuilder and count what reaches a bridge sink.
/// </summary>
public sealed class MultiFilterSamplingBehaviorTests
{
    [Fact]
    public void Sampling_drops_a_fraction_of_events()
    {
        var captured = new CountingLogger();
        var result = QuickLogBuilder.Create()
            .WithBridge(captured)
            .WithMinimumLevel("trace")
            .WithSampling(4) // keep ~1 in 4
            .BuildAndCommit();

        const int emitted = 4000;
        for (var i = 0; i < emitted; i++)
        {
            result.Logger.Information(LogCategory.App, "evt");
        }

        // Probabilistic: keep ~1/4. Assert a wide band so the test is not flaky but
        // still proves sampling is APPLIED (far fewer than emitted, and non-zero).
        captured.Count.Should().BeGreaterThan(0);
        captured.Count.Should().BeLessThan(emitted / 2);
    }

    [Fact]
    public void Throttling_caps_events_per_window()
    {
        var captured = new CountingLogger();
        var result = QuickLogBuilder.Create()
            .WithBridge(captured)
            .WithMinimumLevel("trace")
            .WithThrottling(50) // at most 50 per second
            .BuildAndCommit();

        // Burst far more than the cap within one window; the throttle must hold the
        // line. Allow a small margin for the window-boundary race the filter documents.
        for (var i = 0; i < 1000; i++)
        {
            result.Logger.Information(LogCategory.App, "burst");
        }

        captured.Count.Should().BeLessThanOrEqualTo(120); // 50 cap + generous boundary margin
        captured.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void No_sampling_passes_every_event()
    {
        var captured = new CountingLogger();
        var result = QuickLogBuilder.Create()
            .WithBridge(captured)
            .WithMinimumLevel("trace")
            .BuildAndCommit();

        for (var i = 0; i < 200; i++)
        {
            result.Logger.Information(LogCategory.App, "all");
        }

        captured.Count.Should().Be(200);
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
