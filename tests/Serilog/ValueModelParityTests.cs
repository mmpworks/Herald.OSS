#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Events;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog;

/// <summary>
/// G-VM.1/2: Value-model mirror parity tests (Task 8).
/// Exercises LogEventValueProjector.Project via the lazy Properties accessor.
/// </summary>
public sealed class ValueModelParityTests
{
    [Fact]
    public void Scalar_int_projects_to_ScalarValue()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<ValueModelParityTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);
        log.Information("x {V}", 42);

        var captured = sink.GetEvents();
        captured.Should().HaveCount(1);
        var projected = new MMP.Herald.Serilog.Events.LogEvent(captured[0]).Properties;
        projected["V"].Should().BeOfType<ScalarValue>()
            .Which.Value.Should().Be(42);
    }
    [Fact]
    public void Destructured_object_projects_to_StructureValue()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<ValueModelParityTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);
        log.Information("x {@V}", new { Name = "Alice", Age = 30 });

        var projected = new MMP.Herald.Serilog.Events.LogEvent(sink.GetEvents()[0]).Properties;
        projected["V"].Should().BeOfType<StructureValue>();
    }

    [Fact]
    public void Null_projects_to_ScalarValue_null_not_throw()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<ValueModelParityTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);
        log.Information("x {@V}", (object?)null);

        var projected = new MMP.Herald.Serilog.Events.LogEvent(sink.GetEvents()[0]).Properties;
        projected["V"].Should().BeOfType<ScalarValue>()
            .Which.Value.Should().BeNull();
    }

    [Fact]
    public void Cyclic_object_direct_terminates_without_stack_overflow()
    {
        var node = new Node(); node.Next = node;
        var (herald, sink) = TestLoggers.CreateCapturing<ValueModelParityTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);
        log.Information("x {@V}", node);

        var act = () => new MMP.Herald.Serilog.Events.LogEvent(sink.GetEvents()[0]).Properties;
        act.Should().NotThrow();
    }

    [Fact]
    public void Cyclic_object_indirect_terminates_without_stack_overflow()
    {
        var a = new Node();
        var b = new Node();
        a.Next = b;
        b.Next = a;

        var (herald, sink) = TestLoggers.CreateCapturing<ValueModelParityTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);
        log.Information("x {@V}", a);

        var act = () => new MMP.Herald.Serilog.Events.LogEvent(sink.GetEvents()[0]).Properties;
        act.Should().NotThrow();
    }
    [Fact]
    public void Extra_level_security_maps_to_Warning_and_preserves_HeraldLevel()
    {
        // Construct a native LogEvent directly with the security level
        // (bypasses the pipeline registry, which only knows the 6 standard levels).
        var props = new LogProperty[] { new("V", "sensitive") };
        var native = new MMP.Herald.Events.LogEvent(
            TimeUtc: DateTimeOffset.UtcNow,
            Level: KnownLogLevels.Security,
            Category: LogCategory.None,
            MessageTemplate: "security event {V}",
            Message: "security event sensitive",
            Properties: props,
            Context: MMP.Herald.Events.LogEvent.EmptyContext);

        var mirror = new MMP.Herald.Serilog.Events.LogEvent(native);
        mirror.Level.Should().Be(LogEventLevel.Warning,
            "security maps to Warning per S-1 ruling");

        var mirrorProps = mirror.Properties;
        mirrorProps.Should().ContainKey("HeraldLevel");
        mirrorProps["HeraldLevel"].Should().BeOfType<ScalarValue>()
            .Which.Value.Should().Be("security");
    }
    [Fact]
    public void Stringify_mode_captures_ToString_value()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<ValueModelParityTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);
        log.Information("time {$T}", new DateTime(2024, 1, 15));

        var projected = new MMP.Herald.Serilog.Events.LogEvent(sink.GetEvents()[0]).Properties;
        projected["T"].Should().BeOfType<ScalarValue>()
            .Which.Value.Should().BeOfType<string>("stringify mode converts to string");
    }

    [Fact]
    public void Mutators_add_update_remove_properties()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<ValueModelParityTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);
        log.Information("x {A}", 1);

        var mirror = new MMP.Herald.Serilog.Events.LogEvent(sink.GetEvents()[0]);

        mirror.AddOrUpdateProperty(new LogEventProperty("B", new ScalarValue(99)));
        mirror.Properties.Should().ContainKey("B");
        mirror.AddOrUpdateProperty(new LogEventProperty("A", new ScalarValue(42)));
        ((ScalarValue)mirror.Properties["A"]).Value.Should().Be(42);

        mirror.AddPropertyIfAbsent(new LogEventProperty("A", new ScalarValue(999)));
        ((ScalarValue)mirror.Properties["A"]).Value.Should().Be(42, "existing A not overwritten");
        mirror.AddPropertyIfAbsent(new LogEventProperty("C", new ScalarValue("new")));
        mirror.Properties.Should().ContainKey("C");

        mirror.RemovePropertyIfPresent("B");
        mirror.Properties.Should().NotContainKey("B");
        var noThrow = () => mirror.RemovePropertyIfPresent("NonExistent");
        noThrow.Should().NotThrow();
    }
    [Fact]
    public void Mirror_Level_does_not_throw_for_any_known_level()
    {
        // Build native LogEvents directly so we can test extra (non-standard) levels
        // without needing them registered in the pipeline registry.
        var allLevels = new[] {
            KnownLogLevels.Verbose, KnownLogLevels.Debug,
            KnownLogLevels.Information, KnownLogLevels.Warning, KnownLogLevels.Error,
            KnownLogLevels.Fatal, KnownLogLevels.Security, KnownLogLevels.Notice,
            KnownLogLevels.Success, KnownLogLevels.Metric };

        foreach (var lvl in allLevels)
        {
            var native = new MMP.Herald.Events.LogEvent(
                TimeUtc: DateTimeOffset.UtcNow,
                Level: lvl,
                Category: LogCategory.None,
                MessageTemplate: "test",
                Message: "test",
                Properties: MMP.Herald.Events.LogEvent.EmptyProperties,
                Context: MMP.Herald.Events.LogEvent.EmptyContext);

            var mirror = new MMP.Herald.Serilog.Events.LogEvent(native);
            var act = () => { var _ = mirror.Level; };
            act.Should().NotThrow($"Level getter must not throw for Herald level {lvl.Key}");
        }
    }

    private sealed class Node { public Node? Next; }
}

