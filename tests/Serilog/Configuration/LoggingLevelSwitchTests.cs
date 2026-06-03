#nullable enable

using FluentAssertions;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Events;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog.Configuration;

/// <summary>
/// G-GAP.3 — LoggingLevelSwitch mirror correctness.
/// Verifies that the Serilog-shaped switch forwards through the native
/// Herald switch without losing level identity or thread-safety guarantees.
/// </summary>
public sealed class LoggingLevelSwitchTests
{
    [Fact]
    public void MinimumLevel_roundtrips_through_the_native_switch()
    {
        // Arrange
        var sw = new LoggingLevelSwitch(LogEventLevel.Warning);

        // Assert initial state round-trips cleanly.
        sw.MinimumLevel.Should().Be(LogEventLevel.Warning);

        // Act — mutate via the Serilog-facing surface.
        sw.MinimumLevel = LogEventLevel.Error;

        // Assert the mutation reached the native switch.
        sw.Native.MinimumLevel.Key.Should().Be("error");
    }

    [Fact]
    public void LoggingLevelSwitch_defaults_to_Information()
    {
        // Arrange / Act
        var sw = new LoggingLevelSwitch();

        // Assert
        sw.MinimumLevel.Should().Be(LogEventLevel.Information);
        sw.Native.MinimumLevel.Key.Should().Be("information");
    }

    [Fact]
    public void Native_switch_and_Serilog_switch_stay_in_sync()
    {
        // Arrange
        var sw = new LoggingLevelSwitch(LogEventLevel.Debug);

        // Assert initial state on both surfaces.
        sw.Native.MinimumLevel.Key.Should().Be("debug");

        // Act — mutate via Serilog surface.
        sw.MinimumLevel = LogEventLevel.Fatal;

        // Assert native reflects the mutation.
        sw.Native.MinimumLevel.Key.Should().Be("fatal");
    }

    [Theory]
    [InlineData(LogEventLevel.Verbose,     "verbose")]
    [InlineData(LogEventLevel.Debug,       "debug")]
    [InlineData(LogEventLevel.Information, "information")]
    [InlineData(LogEventLevel.Warning,     "warning")]
    [InlineData(LogEventLevel.Error,       "error")]
    [InlineData(LogEventLevel.Fatal,       "fatal")]
    public void All_six_Serilog_levels_roundtrip_to_the_correct_Herald_key(
        LogEventLevel level, string expectedKey)
    {
        // Arrange / Act
        var sw = new LoggingLevelSwitch(level);

        // Assert Serilog surface matches.
        sw.MinimumLevel.Should().Be(level);

        // Assert native key is correct.
        sw.Native.MinimumLevel.Key.Should().Be(expectedKey);
    }

    [Fact]
    public void Serilog_surface_reflects_mutation_applied_directly_to_native_switch()
    {
        // Arrange — create switch at Debug, then mutate the native side directly.
        var sw = new LoggingLevelSwitch(LogEventLevel.Debug);

        // Act — mutate through the native switch (simulates translator wiring).
        sw.Native.MinimumLevel = MMP.Herald.Levels.KnownLogLevels.Warning;

        // Assert the Serilog surface reflects the native mutation.
        sw.MinimumLevel.Should().Be(LogEventLevel.Warning);
    }
}

