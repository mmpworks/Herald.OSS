#nullable enable

using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// Sub-minimum events must not reach the sinks. The pipeline's minimum
/// level is the cheapest filter in the chain; events at a lower rank
/// are rejected before any per-sink dispatch.
/// </summary>
public sealed class LevelFilterTests
{
    [Fact]
    public void Events_below_minimum_level_do_not_reach_the_bridge_sink()
    {
        var captured = new CapturingLogger();

        var result = QuickLogBuilder.Create()
            .WithBridge(captured)
            .WithMinimumLevel("warn")
            .BuildAndCommit();

        // Debug + info are below warn — should be rejected.
        result.Logger.Debug(LogCategory.App, "below-min-debug");
        result.Logger.Info(LogCategory.App, "below-min-info");

        // Warn + error are at-or-above warn — should pass.
        result.Logger.Warn(LogCategory.App, "above-min-warn");
        result.Logger.Error(LogCategory.App, "above-min-error");

        captured.Messages.Should().NotContain(m => m == "below-min-debug");
        captured.Messages.Should().NotContain(m => m == "below-min-info");

        captured.Messages.Should().ContainSingle(m => m == "above-min-warn");
        captured.Messages.Should().ContainSingle(m => m == "above-min-error");
    }

    [Fact]
    public void Minimum_level_trace_passes_every_event()
    {
        var captured = new CapturingLogger();

        var result = QuickLogBuilder.Create()
            .WithBridge(captured)
            .WithMinimumLevel("trace")
            .BuildAndCommit();

        result.Logger.Trace(LogCategory.App, "t");
        result.Logger.Debug(LogCategory.App, "d");
        result.Logger.Info(LogCategory.App, "i");
        result.Logger.Warn(LogCategory.App, "w");
        result.Logger.Error(LogCategory.App, "e");

        captured.Messages.Should().HaveCount(5);
    }

    private sealed class CapturingLogger : ILogger
    {
        public ConcurrentBag<string> Messages { get; } = new();

        public void Log(LogEvent logEvent) => Messages.Add(logEvent.Message);

        public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
        {
            Messages.Add(logEvent.Message);
            return ValueTask.CompletedTask;
        }
    }
}
