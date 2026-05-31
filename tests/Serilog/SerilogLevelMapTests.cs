#nullable enable
#if NET9_0_OR_GREATER

using FluentAssertions;
using MMP.Herald.Levels;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Events;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog;

public sealed class SerilogLevelMapTests
{
    [Theory]
    [InlineData(LogEventLevel.Verbose,     "verbose")]
    [InlineData(LogEventLevel.Debug,       "debug")]
    [InlineData(LogEventLevel.Information, "information")]
    [InlineData(LogEventLevel.Warning,     "warning")]
    [InlineData(LogEventLevel.Error,       "error")]
    [InlineData(LogEventLevel.Fatal,       "fatal")]
    public void ToHerald_maps_every_Serilog_level(LogEventLevel level, string expectedKey)
        => SerilogLevelMap.ToHerald(level).Key.Should().Be(expectedKey);

    [Theory]
    [InlineData("verbose",     LogEventLevel.Verbose)]
    [InlineData("debug",       LogEventLevel.Debug)]
    [InlineData("information", LogEventLevel.Information)]
    [InlineData("warning",     LogEventLevel.Warning)]
    [InlineData("error",       LogEventLevel.Error)]
    [InlineData("fatal",       LogEventLevel.Fatal)]
    public void TryToSerilog_maps_the_overlapping_six(string key, LogEventLevel expected)
    {
        var heraldLevel = key switch
        {
            "verbose"     => KnownLogLevels.Verbose,
            "debug"       => KnownLogLevels.Debug,
            "information" => KnownLogLevels.Information,
            "warning"     => KnownLogLevels.Warning,
            "error"       => KnownLogLevels.Error,
            _             => KnownLogLevels.Fatal
        };
        SerilogLevelMap.TryToSerilog(heraldLevel, out var level).Should().BeTrue();
        level.Should().Be(expected);
    }

    [Theory]
    [InlineData("notice")]
    [InlineData("success")]
    [InlineData("security")]
    [InlineData("metric")]
    public void ExtraLevels_have_no_Serilog_counterpart(string key)
    {
        var extraLevel = new MMP.Herald.Levels.LogLevel(key, key);
        SerilogLevelMap.TryToSerilog(extraLevel, out _).Should().BeFalse();
    }
}

#endif
