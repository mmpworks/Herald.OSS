#nullable enable
#if NET9_0_OR_GREATER

using FluentAssertions;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Configuration;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Events;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog.Configuration;

/// <summary>
/// Pins that <c>MinimumLevel.Override</c> routes through the kernel-fast
/// <c>WithFastDynamicLevel</c> path, never the legacy <c>DynamicLevels</c> path.
///
/// <para>
/// Richard's R-4 ratification: "Override routes through kernel-fast
/// WithFastDynamicLevel(switch, map) — NOT the legacy DynamicLevelPolicy path."
/// </para>
///
/// <para>
/// JSON field names (camelCase, from <c>LoggingJsonSerializer</c> + <c>JsonLoggingConfig</c>):
/// <list type="bullet">
///   <item><description>
///     <c>"fastPathDynamicLevel"</c> — kernel-eligible fast path. When Override routes
///     correctly this block is non-null (contains <c>"initialLevel"</c> and
///     <c>"categories"</c>).
///   </description></item>
///   <item><description>
///     <c>"dynamicLevels"</c> — always serialised by <c>JsonLoggingConfig</c> but as
///     <c>null</c> when the legacy path is inactive. The regression pin is that this
///     field remains <c>null</c> (i.e. the JSON contains <c>"dynamicLevels": null</c>,
///     NOT a non-null object with <c>"enabled": true</c>).
///   </description></item>
/// </list>
/// </para>
/// </summary>
public sealed class OverrideRegressionTests
{
    // ExportConfigJson() requires at least one sink. A null sink satisfies
    // validation without affecting the level JSON under test.
    private static LoggerConfiguration WithNullSink(LoggerConfiguration c)
    {
        c.WriteTo.Null();
        return c;
    }

    // ── R-4: fast-path JSON block active, legacy block null ───────────────
    //
    // "dynamicLevels" is always present in the JSON (it's a required record
    // field, serialised as null when inactive). The regression pin is that
    // Override never activates the legacy path: the JSON must contain
    // "dynamicLevels": null  (inactive) and a non-null fastPathDynamicLevel
    // block (active). We assert "dynamicLevels\": null" to distinguish the
    // inactive-null state from an active legacy block (which would serialise
    // as "dynamicLevels\": {\"enabled\":true,...}).

    [Fact]
    public void Override_produces_fastPathDynamicLevel_block_not_legacy_dynamicLevels_block()
    {
        var config = WithNullSink(new LoggerConfiguration());
        config.MinimumLevel.Override("Microsoft", LogEventLevel.Warning);

        var json = config.Builder.ExportConfigJson();

        // The kernel-fast path block must be non-null (Override wired correctly).
        json.Should().Contain("\"fastPathDynamicLevel\": {",
            "Override must route through WithFastDynamicLevel so the fast-path block is non-null in JSON");

        // The legacy path must be inactive: serialised as null, never as an active object.
        json.Should().Contain("\"dynamicLevels\": null",
            "dynamicLevels must remain null — an active legacy block would disqualify the kernel fast path");
    }

    // ── Category overrides round-trip inside fastPathDynamicLevel ─────────

    [Fact]
    public void Override_category_name_and_level_appear_inside_fast_path_block()
    {
        var config = WithNullSink(new LoggerConfiguration());
        config.MinimumLevel.Override("Microsoft", LogEventLevel.Warning);

        var json = config.Builder.ExportConfigJson();

        // Category name and Herald key must survive serialisation.
        json.Should().Contain("Microsoft",
            "Override category name must appear in the fast-path JSON");
        json.Should().Contain("\"warning\"",
            "Override level must be serialised as the Herald key 'warning'");
    }

    [Fact]
    public void Multiple_overrides_all_appear_inside_fast_path_block()
    {
        var config = WithNullSink(new LoggerConfiguration());
        config.MinimumLevel
            .Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Error);

        var json = config.Builder.ExportConfigJson();

        json.Should().Contain("Microsoft", "first override must survive into the fast-path JSON");
        json.Should().Contain("System", "second override must accumulate, not replace, the first");
        // Legacy path must remain inactive.
        json.Should().Contain("\"dynamicLevels\": null",
            "multiple overrides must still use the fast path; legacy block must remain null");
    }

    // ── ControlledBy + Override ordering: both use fast path ─────────────

    [Fact]
    public void ControlledBy_then_Override_uses_fast_path()
    {
        var sw = new LoggingLevelSwitch(LogEventLevel.Debug);
        var config = WithNullSink(new LoggerConfiguration());
        config.MinimumLevel
            .ControlledBy(sw)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning);

        var json = config.Builder.ExportConfigJson();

        json.Should().Contain("\"fastPathDynamicLevel\": {",
            "ControlledBy-then-Override must use the kernel fast path");
        json.Should().Contain("Microsoft",
            "the Override category must appear in the fast-path JSON");
        json.Should().Contain("\"dynamicLevels\": null",
            "legacy path must remain inactive");
    }

    [Fact]
    public void Override_then_ControlledBy_uses_fast_path()
    {
        var sw = new LoggingLevelSwitch(LogEventLevel.Debug);
        var config = WithNullSink(new LoggerConfiguration());
        config.MinimumLevel
            .Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.ControlledBy(sw);

        var json = config.Builder.ExportConfigJson();

        json.Should().Contain("\"fastPathDynamicLevel\": {",
            "Override-then-ControlledBy must use the kernel fast path");
        json.Should().Contain("Microsoft",
            "the Override category must survive the ControlledBy call");
        json.Should().Contain("\"dynamicLevels\": null",
            "legacy path must remain inactive");
    }

    // ── ReadFrom accessor is present on LoggerConfiguration ──────────────
    // R-4 accessor: ReadFrom returns this (the LoggerConfiguration itself) so
    // P5 can attach ReadFrom.Configuration(IConfiguration) as an extension method.

    [Fact]
    public void ReadFrom_accessor_returns_the_same_LoggerConfiguration_instance()
    {
        var config = new LoggerConfiguration();

        // ReadFrom returns this — P5 hangs extension methods off it.
        config.ReadFrom.Should().BeSameAs(config,
            "ReadFrom is a fluent anchor that returns this; P5 attaches Configuration() as an extension method on LoggerConfiguration");
    }

    [Fact]
    public void ReadFrom_chain_does_not_break_subsequent_fluent_calls()
    {
        // Accessing ReadFrom must not mutate builder state or break the chain.
        var config = WithNullSink(new LoggerConfiguration());
        var act = () =>
        {
            _ = config.ReadFrom;   // P5 would call ReadFrom.Configuration(...) here
            config.MinimumLevel.Information();
        };

        act.Should().NotThrow("ReadFrom must be a pure accessor with no side effects");
    }
}

#endif
