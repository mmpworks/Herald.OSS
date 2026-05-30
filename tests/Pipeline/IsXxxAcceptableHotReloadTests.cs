#nullable enable

using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// Pins the per-known-level accept booleans on <see cref="StructuredLogger"/>
/// to the live minimum level, including after a level-only hot reload.
///
/// <para>
/// The accept booleans used to be <c>readonly</c> fields set once at
/// construction; the level-only hot-reload path updated
/// <c>_currentGlobalSwitch.MinimumLevel</c> but left those fields pinned to
/// the original minimum. Source-gen-emitted code reading
/// <c>logger.IsDebugAcceptable</c> kept the stale value, so a level-only
/// reload that lowered the minimum silently dropped events that should have
/// been accepted. The fix is the field → property flip plus a
/// <c>RecomputeAcceptables</c> hook the level-only reload now calls.
/// </para>
/// </summary>
public sealed class IsXxxAcceptableHotReloadTests
{
    [Fact]
    public void Construction_with_information_minimum_rejects_debug()
    {
        var result = QuickLogBuilder.Create()
            .WithBridge(new CapturingLogger())
            .WithMinimumLevel(KnownLogLevels.Information.Key)
            .BuildAndCommit();

        result.Logger.IsTraceAcceptable.Should().BeFalse();
        result.Logger.IsDebugAcceptable.Should().BeFalse();
        result.Logger.IsInfoAcceptable.Should().BeTrue();
        result.Logger.IsWarnAcceptable.Should().BeTrue();
        result.Logger.IsErrorAcceptable.Should().BeTrue();
    }

    [Fact]
    public void RecomputeAcceptables_drops_floor_and_flips_debug_to_true()
    {
        var result = QuickLogBuilder.Create()
            .WithBridge(new CapturingLogger())
            .WithMinimumLevel(KnownLogLevels.Information.Key)
            .BuildAndCommit();

        result.Logger.IsDebugAcceptable.Should().BeFalse();

        // Simulate the level-only hot-reload path: lower the minimum.
        result.Logger.RecomputeAcceptables(KnownLogLevels.Debug);

        result.Logger.IsDebugAcceptable.Should().BeTrue();
        result.Logger.IsInfoAcceptable.Should().BeTrue();
    }

    [Fact]
    public void RecomputeAcceptables_raises_floor_and_flips_warn_to_false()
    {
        var result = QuickLogBuilder.Create()
            .WithBridge(new CapturingLogger())
            .WithMinimumLevel(KnownLogLevels.Information.Key)
            .BuildAndCommit();

        result.Logger.IsWarnAcceptable.Should().BeTrue();
        result.Logger.IsInfoAcceptable.Should().BeTrue();

        // Raise the minimum to Error — Warn and Info should now reject.
        result.Logger.RecomputeAcceptables(KnownLogLevels.Error);

        result.Logger.IsWarnAcceptable.Should().BeFalse();
        result.Logger.IsInfoAcceptable.Should().BeFalse();
        result.Logger.IsErrorAcceptable.Should().BeTrue();
    }

    [Fact]
    public void RecomputeAcceptables_with_null_minimum_accepts_everything()
    {
        var result = QuickLogBuilder.Create()
            .WithBridge(new CapturingLogger())
            .WithMinimumLevel(KnownLogLevels.Error.Key)
            .BuildAndCommit();

        result.Logger.IsDebugAcceptable.Should().BeFalse();

        result.Logger.RecomputeAcceptables(null);

        result.Logger.IsTraceAcceptable.Should().BeTrue();
        result.Logger.IsDebugAcceptable.Should().BeTrue();
        result.Logger.IsInfoAcceptable.Should().BeTrue();
        result.Logger.IsWarnAcceptable.Should().BeTrue();
        result.Logger.IsErrorAcceptable.Should().BeTrue();
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
