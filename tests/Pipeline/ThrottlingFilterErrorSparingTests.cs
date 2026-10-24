#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Filters;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// Throttling spares errors by default. Once a window is full, a Warning is dropped but an
/// Error, Fatal or Security event still passes. Spared events are not counted, so a burst of
/// errors cannot use up the budget that lower levels share. <c>throttleErrors: true</c>
/// restores the rule where errors are counted and dropped like any other event.
/// </summary>
public sealed class ThrottlingFilterErrorSparingTests
{
    // A one-hour window keeps every filter-level test inside a single window.
    private static readonly TimeSpan LongWindow = TimeSpan.FromHours(1);

    private const int BurstSize = 10;

    // ---- Filter level ---------------------------------------------------

    [Fact]
    public void Full_window_drops_a_warning_but_passes_error_and_above()
    {
        var filter = new ThrottlingFilter(maxEventsPerWindow: 2, LongWindow);

        filter.Allow(Event(KnownLogLevels.Information)).Should().BeTrue();
        filter.Allow(Event(KnownLogLevels.Information)).Should().BeTrue();

        filter.Allow(Event(KnownLogLevels.Warning)).Should().BeFalse("the window is full");
        filter.Allow(Event(KnownLogLevels.Error)).Should().BeTrue("errors skip the limiter");
        filter.Allow(Event(KnownLogLevels.Fatal)).Should().BeTrue("Fatal ranks above Error");
        filter.Allow(Event(KnownLogLevels.Security)).Should().BeTrue("Security ranks above Error");
    }

    [Fact]
    public void Spared_errors_do_not_use_up_the_window_count()
    {
        var filter = new ThrottlingFilter(maxEventsPerWindow: 2, LongWindow);

        for (var i = 0; i < 50; i++)
        {
            filter.Allow(Event(KnownLogLevels.Error)).Should().BeTrue();
        }

        filter.Allow(Event(KnownLogLevels.Warning)).Should().BeTrue("errors did not consume the budget");
        filter.Allow(Event(KnownLogLevels.Warning)).Should().BeTrue("errors did not consume the budget");
        filter.Allow(Event(KnownLogLevels.Warning)).Should().BeFalse("the third warning exceeds the cap of 2");
    }

    [Fact]
    public void Error_level_follows_the_given_registry_and_error_level()
    {
        var filter = new ThrottlingFilter(
            maxEventsPerWindow: 1,
            LongWindow,
            scope: null,
            levelRegistry: new DefaultLogLevelRegistryFactory().Create(),
            errorLevel: KnownLogLevels.Fatal);

        filter.Allow(Event(KnownLogLevels.Information)).Should().BeTrue();
        filter.Allow(Event(KnownLogLevels.Error)).Should().BeFalse("Error ranks below the Fatal error level");
        filter.Allow(Event(KnownLogLevels.Fatal)).Should().BeTrue();
    }

    [Fact]
    public void Opt_out_throttles_errors_like_every_other_event()
    {
        var filter = new ThrottlingFilter(
            maxEventsPerWindow: 2,
            LongWindow,
            scope: null,
            levelRegistry: new DefaultLogLevelRegistryFactory().Create(),
            throttleErrors: true);

        filter.ThrottlesErrors.Should().BeTrue();
        filter.Allow(Event(KnownLogLevels.Error)).Should().BeTrue();
        filter.Allow(Event(KnownLogLevels.Error)).Should().BeTrue();
        filter.Allow(Event(KnownLogLevels.Error)).Should().BeFalse("errors count toward the cap");
        filter.Allow(Event(KnownLogLevels.Fatal)).Should().BeFalse("errors count toward the cap");
        filter.Allow(Event(KnownLogLevels.Warning)).Should().BeFalse("errors used up the budget");
    }

    [Fact]
    public void Default_constructor_spares_errors()
    {
        new ThrottlingFilter(maxEventsPerWindow: 1, LongWindow).ThrottlesErrors.Should().BeFalse();
    }

    [Fact]
    public void Allow_allocates_zero_bytes_for_spared_and_throttled_events()
    {
        var filter = new ThrottlingFilter(maxEventsPerWindow: 2, LongWindow);
        var error = Event(KnownLogLevels.Error);
        var warning = Event(KnownLogLevels.Warning);

        // Warm up so JIT and first-call costs are outside the measurement.
        for (var i = 0; i < 100; i++)
        {
            filter.Allow(error);
            filter.Allow(warning);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            filter.Allow(error);
            filter.Allow(warning);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0);
    }

    // ---- QuickLogBuilder.WithThrottling, end to end --------------------

    [Fact]
    public void WithThrottling_passes_every_error_and_still_caps_warnings()
    {
        var sink = new LevelCountingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("verbose")
            .WithThrottling(2)
            .BuildAndCommit();

        LogErrorBurstThenWarnings(result.Logger);

        sink.Count(KnownLogLevels.Error).Should().Be(BurstSize, "errors skip the throttle");
        sink.Count(KnownLogLevels.Warning).Should().BeGreaterThan(0, "errors did not consume the budget");
        // Up to 2 per window; a window boundary during the test can open one more window.
        sink.Count(KnownLogLevels.Warning).Should().BeLessThanOrEqualTo(4);
    }

    [Fact]
    public void WithThrottling_opt_out_throttles_errors()
    {
        var sink = new LevelCountingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("verbose")
            .WithThrottling(2, throttleErrors: true)
            .BuildAndCommit();

        LogErrorBurstThenWarnings(result.Logger);

        sink.Count(KnownLogLevels.Error).Should().BeLessThanOrEqualTo(4, "errors are throttled");
    }

    // ---- JSON MaxPerSecond rule: JSON text -> serializer -> mapper -> filter ----
    // QuickLogBuilder.WithThrottling writes the same JsonSamplingRule and runs the same
    // mapper, so the builder tests above cover the logger plumbing after the mapper.

    [Fact]
    public void Json_MaxPerSecond_rule_passes_every_error_and_still_caps_warnings()
    {
        var filter = ThrottleFilterFromJson("""{ "maxPerSecond": 2 }""");

        filter.Allow(Event(KnownLogLevels.Warning)).Should().BeTrue();
        filter.Allow(Event(KnownLogLevels.Warning)).Should().BeTrue();
        filter.Allow(Event(KnownLogLevels.Warning)).Should().BeFalse("the window is full");
        filter.Allow(Event(KnownLogLevels.Error)).Should().BeTrue("errors skip the throttle");
        filter.Allow(Event(KnownLogLevels.Fatal)).Should().BeTrue("Fatal ranks above Error in the pipeline registry");
    }

    [Fact]
    public void Json_ThrottleErrors_true_throttles_errors()
    {
        var filter = ThrottleFilterFromJson("""{ "maxPerSecond": 2, "throttleErrors": true }""");

        filter.ThrottlesErrors.Should().BeTrue();
        filter.Allow(Event(KnownLogLevels.Error)).Should().BeTrue();
        filter.Allow(Event(KnownLogLevels.Error)).Should().BeTrue();
        filter.Allow(Event(KnownLogLevels.Error)).Should().BeFalse("errors count toward the cap");
    }

    // ---- Helpers --------------------------------------------------------

    // Parses a config document holding one sampling rule and maps it with the default
    // pipeline registry. The rule runs inside one second, so it stays in one window
    // unless the test machine stalls.
    private static ThrottlingFilter ThrottleFilterFromJson(string ruleJson)
    {
        var json = $$"""{ "sampling": { "enabled": true, "rules": [ {{ruleJson}} ] } }""";
        var config = MMP.Herald.Configuration.LoggingJsonSerializer.Deserialize(json);
        var filters = MMP.Herald.Configuration.DefaultLoggingConfigurationMapper.MapForTest(
            config.Sampling, new DefaultLogLevelRegistryFactory().Create());

        filters.Should().NotBeNull().And.HaveCount(1);
        return filters![0].Should().BeOfType<ThrottlingFilter>().Subject;
    }

    private static void LogErrorBurstThenWarnings(StructuredLogger logger)
    {
        for (var i = 0; i < BurstSize; i++)
        {
            logger.Error("op {Op} failed", i);
        }

        for (var i = 0; i < BurstSize; i++)
        {
            logger.Warning("disk {Percent} full", i);
        }
    }

    private static LogEvent Event(LogLevel level) =>
        new(
            TimeUtc: DateTimeOffset.UtcNow,
            Level: level,
            Category: LogCategory.App,
            MessageTemplate: "m",
            Message: "m",
            Properties: LogEvent.EmptyProperties,
            Context: LogEvent.EmptyContext);

    private sealed class LevelCountingBridge : ILogger
    {
        private readonly List<LogEvent> _events = new();
        private readonly object _sync = new();

        public int Count(LogLevel level)
        {
            lock (_sync)
            {
                return _events.Count(e => string.Equals(e.Level.Key, level.Key, StringComparison.OrdinalIgnoreCase));
            }
        }

        public void Log(LogEvent logEvent)
        {
            lock (_sync) { _events.Add(logEvent); }
        }

        public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
        {
            Log(logEvent);
            return ValueTask.CompletedTask;
        }
    }
}
