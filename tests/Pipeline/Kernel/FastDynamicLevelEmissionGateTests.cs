#nullable enable

using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline.Kernel;

/// <summary>
/// Emission-boundary regression for the dynamic global level floor.
///
/// <para>
/// W2-blocker (Glenn, Lane A): the Serilog <c>MinimumLevel.ControlledBy</c>
/// overload wires a runtime <c>LoggingLevelSwitch</c> through
/// <see cref="QuickLogBuilder.WithFastDynamicLevel(LogLevelSwitch)"/>. Before
/// this fix, the dynamic floor did not gate emission: an Information event
/// under a Warning dynamic floor still reached the sink, and
/// <c>IsEnabled(Information)</c> returned <c>true</c>. That broke the common
/// Serilog runtime-switch contract and would have turned W2 into a global
/// Information leak whenever appsettings carried both a Default floor and an
/// Override block.
/// </para>
///
/// <para>
/// These assertions live at the sink boundary — they count what a sink
/// actually receives, not what the config JSON declares. The gate decision
/// must be identical on every dispatch path (W6 parity): the buffer/kernel
/// path, the heap/chain path, and the <c>IsEnabled</c> query all consult the
/// dynamic switch the same way the static floor is consulted.
/// </para>
/// </summary>
public sealed class FastDynamicLevelEmissionGateTests
{
    // A capturing bridge sink. Counts every event that crosses the emission
    // boundary regardless of which dispatch path (buffer or heap) delivered it.
    private sealed class CountingBridge : MMP.Herald.ILogger
    {
        private int _count;
        public int Count => System.Threading.Volatile.Read(ref _count);
        public void Log(LogEvent logEvent) => System.Threading.Interlocked.Increment(ref _count);
        public System.Threading.Tasks.ValueTask LogAsync(
            LogEvent logEvent, System.Threading.CancellationToken cancellationToken = default)
        {
            System.Threading.Interlocked.Increment(ref _count);
            return System.Threading.Tasks.ValueTask.CompletedTask;
        }
    }

    // Builds a logger gated ONLY by a dynamic switch — no static floor. That
    // isolates the dynamic gate: any leak here is the dynamic switch failing,
    // not a static floor masking the result.
    private static (StructuredLogger logger, CountingBridge sink, LogLevelSwitch dynamicSwitch)
        BuildDynamicOnly(LogLevel initialFloor)
    {
        var sink = new CountingBridge();
        var dynamicSwitch = new LogLevelSwitch(initialFloor);
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithFastDynamicLevel(dynamicSwitch)
            .SuppressNamingPolicyAnnouncement()
            .BuildAndCommit();
        return (result.Logger, sink, dynamicSwitch);
    }

    private static (StructuredLogger logger, CountingBridge sink,
                    LogLevelSwitch dynamicSwitch, CategoryLevelSwitchMap categoryMap)
        BuildDynamicWithCategory(LogLevel globalFloor, string category, LogLevel categoryFloor)
    {
        var sink = new CountingBridge();
        var dynamicSwitch = new LogLevelSwitch(globalFloor);
        var categoryMap = new CategoryLevelSwitchMap();
        categoryMap.SetCategoryLevel(category, categoryFloor);
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithFastDynamicLevel(dynamicSwitch, categoryMap)
            .SuppressNamingPolicyAnnouncement()
            .BuildAndCommit();
        return (result.Logger, sink, dynamicSwitch, categoryMap);
    }

    // ── Global dynamic floor gates emission ────────────────────────────────

    [Fact]
    public void Dynamic_floor_at_Warning_blocks_Information_at_the_sink()
    {
        var (logger, sink, _) = BuildDynamicOnly(KnownLogLevels.Warning);

        logger.Information(LogCategory.App, "below the dynamic floor");

        sink.Count.Should().Be(0,
            "an Information event under a Warning dynamic floor must never reach the sink");
    }

    [Fact]
    public void Dynamic_floor_at_Warning_admits_Warning_at_the_sink()
    {
        var (logger, sink, _) = BuildDynamicOnly(KnownLogLevels.Warning);

        logger.Warning(LogCategory.App, "at the dynamic floor");

        sink.Count.Should().Be(1,
            "a Warning event at the Warning dynamic floor must reach the sink");
    }

    [Fact]
    public void IsEnabled_consults_the_dynamic_floor()
    {
        var (logger, _, _) = BuildDynamicOnly(KnownLogLevels.Warning);

        logger.IsEnabled(KnownLogLevels.Information).Should().BeFalse(
            "IsEnabled(Information) must return false under a Warning dynamic floor — " +
            "the public IsEnabled API is the gate a Serilog runtime-switch user relies on");
        logger.IsEnabled(KnownLogLevels.Warning).Should().BeTrue(
            "IsEnabled(Warning) must return true at the Warning dynamic floor");
    }

    // ── Runtime mutation: the gate follows the switch ──────────────────────

    [Fact]
    public void Raising_the_dynamic_floor_at_runtime_starts_blocking_lower_events()
    {
        var (logger, sink, dynamicSwitch) = BuildDynamicOnly(KnownLogLevels.Information);

        logger.Information(LogCategory.App, "admitted at the Information floor");
        sink.Count.Should().Be(1, "Information is admitted while the floor is Information");

        // Raise the floor at runtime — the Serilog LoggingLevelSwitch contract.
        dynamicSwitch.MinimumLevel = KnownLogLevels.Warning;

        logger.Information(LogCategory.App, "now below the raised floor");
        sink.Count.Should().Be(1, "raising the floor to Warning must block the next Information event");

        logger.Warning(LogCategory.App, "at the raised floor");
        sink.Count.Should().Be(2, "Warning must still pass the raised floor");
    }

    [Fact]
    public void Lowering_the_dynamic_floor_at_runtime_starts_admitting_lower_events()
    {
        var (logger, sink, dynamicSwitch) = BuildDynamicOnly(KnownLogLevels.Warning);

        logger.Information(LogCategory.App, "blocked at the Warning floor");
        sink.Count.Should().Be(0, "Information is blocked while the floor is Warning");

        // Lower the floor at runtime.
        dynamicSwitch.MinimumLevel = KnownLogLevels.Debug;

        logger.Information(LogCategory.App, "now above the lowered floor");
        sink.Count.Should().Be(1, "lowering the floor to Debug must admit the next Information event");
    }

    // ── Per-category dynamic override gates emission ───────────────────────

    [Fact]
    public void Category_override_above_global_floor_blocks_matching_category()
    {
        // Global floor Information; the "Microsoft" category is raised to Warning.
        var (logger, sink, _, _) =
            BuildDynamicWithCategory(KnownLogLevels.Information, "Microsoft", KnownLogLevels.Warning);

        logger.Information(new LogCategory("Microsoft"), "below the category override");
        sink.Count.Should().Be(0,
            "an Information event in a category whose override floor is Warning must be blocked");

        logger.Warning(new LogCategory("Microsoft"), "at the category override floor");
        sink.Count.Should().Be(1, "a Warning event in that category must pass the override floor");
    }

    [Fact]
    public void Non_overridden_category_follows_the_global_floor()
    {
        // Global floor Information; only "Microsoft" is overridden to Warning.
        var (logger, sink, _, _) =
            BuildDynamicWithCategory(KnownLogLevels.Information, "Microsoft", KnownLogLevels.Warning);

        logger.Information(new LogCategory("Checkout"), "category without an override");
        sink.Count.Should().Be(1,
            "a category with no override must follow the global Information floor and admit Information");
    }
}
