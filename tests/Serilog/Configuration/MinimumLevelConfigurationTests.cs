#nullable enable
#if NET9_0_OR_GREATER

using FluentAssertions;
using MMP.Herald.Levels;
using MMP.Herald.Quick;
using MMP.Herald.OSS.Tests.Serilog.Configuration;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Configuration;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Events;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog.Configuration;

// C# 12 note: kept class-per-test form for .NET 8/9 compat.
// Upgrade note for C# 14: static abstract members on interfaces could simplify
// the oracle pattern here; keep current form for C# 12 compatibility.

/// <summary>
/// P2 Task 2 — MinimumLevelConfiguration translator correctness.
///
/// <para>
/// Each test builds the same pipeline configuration two ways:
///   1. Via the Serilog-compat translator (LoggerConfiguration → MinimumLevel.*).
///   2. Via direct QuickLogBuilder calls (the ground truth).
/// The TranslatorParityOracle asserts they produce identical JSON.
/// </para>
///
/// <para>
/// Edge cases covered:
///   - The fatal→"fatal" rename trap (Serilog calls it Fatal; old Herald called it Critical).
///   - Override accumulation across two calls.
///   - Both ControlledBy-then-Override and Override-then-ControlledBy orderings
///     produce identical JSON (R-01 fix).
///   - Override without a prior ControlledBy does not crash (R-02 fix).
/// </para>
/// </summary>
public sealed class MinimumLevelConfigurationTests
{
    // ExportConfigJson() requires at least one sink. Use a null sink as the
    // minimum fixture so validation passes without affecting level JSON.
    private static LoggerConfiguration WithNullSink(LoggerConfiguration c)
    {
        c.WriteTo.Null();
        return c;
    }

    private static QuickLogBuilder WithNullSink(QuickLogBuilder b)
    {
        b.WithNullSink();
        return b;
    }

    // ------------------------------------------------------------------
    // 1. Basic verb mapping
    // ------------------------------------------------------------------

    [Fact]
    public void Debug_verb_produces_same_json_as_WithMinimumLevel_debug()
    {
        // Arrange — translator path
        var config = WithNullSink(new LoggerConfiguration());
        config.MinimumLevel.Debug();

        // Arrange — direct path
        var direct = WithNullSink(QuickLogBuilder.Create().WithMinimumLevel("debug"));

        // Assert
        TranslatorParityOracle.AssertSameShape(config.Builder, direct);
    }

    [Fact]
    public void Verbose_verb_produces_same_json_as_WithMinimumLevel_verbose()
    {
        var config = WithNullSink(new LoggerConfiguration());
        config.MinimumLevel.Verbose();

        var direct = WithNullSink(QuickLogBuilder.Create().WithMinimumLevel("verbose"));

        TranslatorParityOracle.AssertSameShape(config.Builder, direct);
    }

    [Fact]
    public void Information_verb_produces_same_json_as_WithMinimumLevel_information()
    {
        var config = WithNullSink(new LoggerConfiguration());
        config.MinimumLevel.Information();

        var direct = WithNullSink(QuickLogBuilder.Create().WithMinimumLevel("information"));

        TranslatorParityOracle.AssertSameShape(config.Builder, direct);
    }

    [Fact]
    public void Warning_verb_produces_same_json_as_WithMinimumLevel_warning()
    {
        var config = WithNullSink(new LoggerConfiguration());
        config.MinimumLevel.Warning();

        var direct = WithNullSink(QuickLogBuilder.Create().WithMinimumLevel("warning"));

        TranslatorParityOracle.AssertSameShape(config.Builder, direct);
    }

    [Fact]
    public void Error_verb_produces_same_json_as_WithMinimumLevel_error()
    {
        var config = WithNullSink(new LoggerConfiguration());
        config.MinimumLevel.Error();

        var direct = WithNullSink(QuickLogBuilder.Create().WithMinimumLevel("error"));

        TranslatorParityOracle.AssertSameShape(config.Builder, direct);
    }

    // P0 rename-trap: Serilog's Fatal must map to Herald's "fatal", NOT "critical".
    [Fact]
    public void Fatal_verb_maps_to_fatal_key_not_critical()
    {
        var config = WithNullSink(new LoggerConfiguration());
        config.MinimumLevel.Fatal();

        var direct = WithNullSink(QuickLogBuilder.Create().WithMinimumLevel("fatal"));

        TranslatorParityOracle.AssertSameShape(config.Builder, direct);
    }

    [Fact]
    public void Is_Fatal_maps_to_fatal_key_not_critical()
    {
        // Arrange — translator path via Is()
        var config = WithNullSink(new LoggerConfiguration());
        config.MinimumLevel.Is(LogEventLevel.Fatal);

        // Arrange — direct path — must be "fatal", NOT "critical"
        var direct = WithNullSink(QuickLogBuilder.Create().WithMinimumLevel("fatal"));

        TranslatorParityOracle.AssertSameShape(config.Builder, direct);
    }

    // ------------------------------------------------------------------
    // 2. ControlledBy — JSON carries the FastPathDynamicLevel block
    // ------------------------------------------------------------------

    [Fact]
    public void ControlledBy_produces_same_json_as_WithFastDynamicLevel()
    {
        // Arrange — translator path
        var sw = new LoggingLevelSwitch(LogEventLevel.Warning);
        var config = WithNullSink(new LoggerConfiguration());
        config.MinimumLevel.ControlledBy(sw);

        // Arrange — direct path
        var direct = WithNullSink(QuickLogBuilder.Create()).WithFastDynamicLevel(sw.Native);

        TranslatorParityOracle.AssertSameShape(config.Builder, direct);
    }

    [Fact]
    public void ControlledBy_json_contains_FastPathDynamicLevel_block()
    {
        var sw = new LoggingLevelSwitch(LogEventLevel.Debug);
        var config = WithNullSink(new LoggerConfiguration());
        config.MinimumLevel.ControlledBy(sw);

        var json = config.Builder.ExportConfigJson();

        // The JSON serializer uses camelCase keys.
        json.Should().Contain("fastPathDynamicLevel",
            "ControlledBy must wire WithFastDynamicLevel so the JSON block is present");
    }

    // ------------------------------------------------------------------
    // 3. Override accumulation — two calls produce BOTH entries
    // ------------------------------------------------------------------

    [Fact]
    public void Two_Override_calls_accumulate_both_category_entries()
    {
        // Arrange — translator path: ControlledBy first, then two overrides
        var sw = new LoggingLevelSwitch(LogEventLevel.Information);
        var config = WithNullSink(new LoggerConfiguration());
        config.MinimumLevel
            .ControlledBy(sw)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Error);

        var json = config.Builder.ExportConfigJson();

        // Both category names must appear in the JSON
        json.Should().Contain("Microsoft",
            "first Override call must write a Microsoft category entry");
        json.Should().Contain("System",
            "second Override call must accumulate, not replace, the System category entry");
    }

    [Fact]
    public void Two_Override_calls_produce_correct_level_keys()
    {
        var sw = new LoggingLevelSwitch(LogEventLevel.Information);
        var config = WithNullSink(new LoggerConfiguration());
        config.MinimumLevel
            .ControlledBy(sw)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Error);

        var json = config.Builder.ExportConfigJson();

        // Level keys must be "warning" / "error", not Serilog display names
        json.Should().Contain("\"warning\"",
            "Microsoft override must map to Herald key 'warning'");
        json.Should().Contain("\"error\"",
            "System override must map to Herald key 'error'");
    }

    // ------------------------------------------------------------------
    // 4. Ordering invariant: ControlledBy-then-Override == Override-then-ControlledBy
    // ------------------------------------------------------------------

    [Fact]
    public void ControlledBy_then_Override_and_Override_then_ControlledBy_produce_identical_json()
    {
        // Arrange — order 1: ControlledBy first
        var sw1 = new LoggingLevelSwitch(LogEventLevel.Information);
        var config1 = WithNullSink(new LoggerConfiguration());
        config1.MinimumLevel
            .ControlledBy(sw1)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning);

        // Arrange — order 2: Override first (R-01 / R-02 fix)
        var sw2 = new LoggingLevelSwitch(LogEventLevel.Information);
        var config2 = WithNullSink(new LoggerConfiguration());
        config2.MinimumLevel
            .Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.ControlledBy(sw2);

        var json1 = config1.Builder.ExportConfigJson();
        var json2 = config2.Builder.ExportConfigJson();

        json1.Should().Be(json2,
            "ControlledBy-then-Override and Override-then-ControlledBy must produce identical JSON (R-01)");
    }

    // ------------------------------------------------------------------
    // 5. Override-without-ControlledBy: must not crash (R-02 fix)
    // ------------------------------------------------------------------

    [Fact]
    public void Override_without_ControlledBy_does_not_throw()
    {
        var config = new LoggerConfiguration();

        // This must not throw, even with no prior ControlledBy()
        var act = () => config.MinimumLevel.Override("Microsoft", LogEventLevel.Warning);

        act.Should().NotThrow("Override without ControlledBy must auto-create a switch (R-02)");
    }

    [Fact]
    public void Override_without_ControlledBy_json_contains_category_override()
    {
        var config = WithNullSink(new LoggerConfiguration());
        config.MinimumLevel.Override("Microsoft", LogEventLevel.Warning);

        var json = config.Builder.ExportConfigJson();

        json.Should().Contain("Microsoft",
            "Override-without-ControlledBy must still write the category override into JSON");
        json.Should().Contain("\"warning\"",
            "Override-without-ControlledBy must use the correct Herald level key");
    }

    [Fact]
    public void Override_without_ControlledBy_produces_same_json_as_ControlledBy_then_Override_with_same_floor()
    {
        // When Override is called without ControlledBy, the auto-switch initialises at
        // Information (the builder default). Calling ControlledBy afterwards with an
        // explicit Information switch must yield identical JSON.

        // Arrange — Override only (auto-switch at Information floor)
        var config1 = WithNullSink(new LoggerConfiguration());
        config1.MinimumLevel.Override("Microsoft", LogEventLevel.Warning);

        // Arrange — explicit ControlledBy(Information) then Override
        var sw = new LoggingLevelSwitch(LogEventLevel.Information);
        var config2 = WithNullSink(new LoggerConfiguration());
        config2.MinimumLevel
            .ControlledBy(sw)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning);

        var json1 = config1.Builder.ExportConfigJson();
        var json2 = config2.Builder.ExportConfigJson();

        json1.Should().Be(json2,
            "auto-created switch from default floor must produce same JSON as explicit Information switch (R-02)");
    }
}

#endif
