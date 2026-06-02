#nullable enable

using System.Linq;
using FluentAssertions;
using MMP.Herald.Levels;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Events;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog;

public sealed class SerilogLoggerSurfaceTests
{
    [Fact]
    public void Write_routes_to_the_mapped_Herald_level()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<SerilogLoggerSurfaceTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);

        log.Write(LogEventLevel.Information, "user {Id}", 7);
        log.Write(LogEventLevel.Fatal, "boom");

        var events = sink.GetEvents();
        events[0].Level.Key.Should().Be("information");
        events[1].Level.Key.Should().Be("fatal");
    }

    [Fact]
    public void IsEnabled_reflects_the_minimum_level()
    {
        var (herald, _) = TestLoggers.CreateCapturing<SerilogLoggerSurfaceTests>(
            minimumLevel: KnownLogLevels.Warning);
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);

        log.IsEnabled(LogEventLevel.Information).Should().BeFalse();
        log.IsEnabled(LogEventLevel.Error).Should().BeTrue();
    }

    [Fact]
    public void Verb_methods_route_correctly()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<SerilogLoggerSurfaceTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);

        log.Verbose("v");
        log.Debug("d");
        log.Information("i");
        log.Warning("w");
        log.Error("e");
        log.Fatal("f");

        var keys = sink.GetEvents().Select(e => e.Level.Key).ToList();
        keys.Should().Equal("verbose", "debug", "information", "warning", "error", "fatal");
    }
}

