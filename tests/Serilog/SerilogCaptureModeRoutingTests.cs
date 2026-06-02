#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Serilog;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog;

/// <summary>
/// G-HOT.3: per-hole capture-mode routing for the slow-path (any non-default hole
/// triggers WriteCore, which must read CaptureMode from the parsed template tokens).
///
/// <para>
/// The load-bearing test is the mixed-hole test. Single-mode tests pass against a
/// broken per-call router that always emits Default; only the mixed template catches
/// the real bug: three holes in one call, each with a different mode.
/// </para>
///
/// <para>
/// Key design constraint: the transport is all-or-nothing. Any non-default hole
/// pushes the entire call through the WriteCore / BuildProperties slow path, which
/// must then set CaptureMode per-hole from the parsed MessageTemplateToken.Property.
/// </para>
/// </summary>
public sealed class SerilogCaptureModeRoutingTests
{
    // THE load-bearing test: mixed-hole template with all three modes in ONE call.
    // Single-mode tests pass against a broken per-call router; only the mixed
    // template catches it — a broken router that always emits Default passes both
    // single-mode tests but fails here.
    [Fact]
    public void Mixed_hole_template_routes_each_hole_by_its_own_capture_mode()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<SerilogCaptureModeRoutingTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);

        // Three holes: default scalar, destructure, stringify — all in one call.
        log.Information("User {Id} ordered {@Order} at {$Time}",
            7,
            new { Sku = "A1", Qty = 2 },
            DateTime.UnixEpoch);

        var events = sink.GetEvents();
        events.Should().HaveCount(1, "exactly one event was logged");

        var props = events[0].Properties;

        // {Id}    — bare hole → Default
        PropFor(props, "Id").CaptureMode.Should().Be(LogPropertyCaptureMode.Default,
            "bare {Id} is a default-mode hole — should be scalar/compact-eligible");

        // {@Order} — '@' prefix → Destructure
        PropFor(props, "Order").CaptureMode.Should().Be(LogPropertyCaptureMode.Destructure,
            "{@Order} must carry Destructure mode — NOT silently flattened to scalar");

        // {$Time}  — '$' prefix → Stringify
        PropFor(props, "Time").CaptureMode.Should().Be(LogPropertyCaptureMode.Stringify,
            "{$Time} must carry Stringify mode");

        // Verify the value of Id survived (slow-path fallback must not drop it).
        PropFor(props, "Id").Value.Should().Be(7,
            "the default-mode value must survive the slow-path WriteCore path unchanged");
    }

    // {$Id} where Id is int: Stringify-of-primitive must NOT be compacted away.
    // A 'smart' optimisation that silently strips $ from primitives is a bug:
    // the caller explicitly asked for Stringify semantics and must get them.
    [Fact]
    public void Stringify_of_primitive_preserves_the_mode()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<SerilogCaptureModeRoutingTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);

        log.Information("id {$Id}", 7);

        var events = sink.GetEvents();
        events.Should().HaveCount(1);

        PropFor(events[0].Properties, "Id").CaptureMode
            .Should().Be(LogPropertyCaptureMode.Stringify,
                "{$} survives even when the value is a primitive int — " +
                "stripping $ from primitives is a bug, not an optimisation");
    }

    // All-default template: verify that hole names from the template (not from
    // CallerArgumentExpression) arrive in the event with the right values.
    // This is the correctness companion to the G-HOT.2 allocation test.
    [Fact]
    public void All_default_holes_produce_correct_property_names_from_the_template()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<SerilogCaptureModeRoutingTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);

        log.Information("User {UserId} logged in from {IpAddress}", 42, "10.0.0.1");

        var events = sink.GetEvents();
        events.Should().HaveCount(1);

        var props = events[0].Properties;
        props.Should().Contain(p => p.Name == "UserId",
            "hole name comes from the template, not CallerArgumentExpression");
        props.Should().Contain(p => p.Name == "IpAddress",
            "second hole name comes from the template");

        PropFor(props, "UserId").Value.Should().Be(42);
        PropFor(props, "IpAddress").Value.Should().Be("10.0.0.1");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    // Locate a property by name and return it. Fails the test if absent.
    private static LogProperty PropFor(IReadOnlyList<LogProperty> props, string name)
    {
        var prop = props.FirstOrDefault(p => p.Name == name);
        // A zeroed LogProperty has Name = null; guard against it explicitly.
        prop.Name.Should().Be(name,
            $"property '{name}' should be present in the event; " +
            $"found names: [{string.Join(", ", props.Select(p => p.Name))}]");
        return prop;
    }
}

