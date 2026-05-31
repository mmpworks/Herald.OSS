#nullable enable
#if NET9_0_OR_GREATER

using FluentAssertions;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Serilog;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog;

/// <summary>
/// Template boundary and escaping integration tests for the Serilog compat layer.
///
/// These tests exercise the WriteCore → MessageTemplateParser → LogEventFactory path
/// and pin edge cases that must not throw or produce corrupt events.
///
/// Terminology:
/// - LogEvent.Message   = the fully rendered string (holes replaced by values).
/// - LogEvent.MessageTemplate = the original template string (holes preserved).
/// - LogEvent.Properties = the bound named properties from template holes.
/// </summary>
public sealed class SerilogTemplateIntegrationTests
{
    // {{ and }} are escape sequences for literal braces.
    // "value is {{42}}" → rendered Message contains "{42}" (one brace pair, no hole).
    // No properties are bound because there are no real holes in the template.
    [Fact]
    public void Template_escape_double_braces_renders_literally()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<SerilogTemplateIntegrationTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);

        log.Information("value is {{42}}");

        var events = sink.GetEvents();
        events.Should().HaveCount(1);

        // Rendered message must contain the literal brace pair, not empty string.
        events[0].Message.Should().Contain("{42}",
            "{{ and }} are escape sequences; the rendered output should contain literal { and }");

        // No real holes → no properties bound.
        events[0].Properties.Should().BeEmpty(
            "escaped braces are not template holes; no properties should be bound");
    }

    // Empty template: must not throw and must produce a valid event with no properties.
    [Fact]
    public void Empty_template_produces_event_without_throwing()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<SerilogTemplateIntegrationTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);

        var act = () => log.Information("");
        act.Should().NotThrow("an empty template is legal and must not crash");

        var events = sink.GetEvents();
        events.Should().HaveCount(1, "the event must still be captured");
        events[0].Properties.Should().BeEmpty("an empty template has no holes to bind");
    }

    // Template with no holes: extra args are silently ignored (matches Serilog's behaviour).
    [Fact]
    public void Template_with_no_holes_ignores_extra_args()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<SerilogTemplateIntegrationTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);

        var act = () => log.Information("no holes here", 1, 2, 3);
        act.Should().NotThrow("extra args with no matching holes must not crash");

        var events = sink.GetEvents();
        events.Should().HaveCount(1);
        events[0].Properties.Should().BeEmpty(
            "no template holes → no properties bound, even when args are supplied");
    }

    // Property names come from template holes, NOT from argument variable names or
    // CallerArgumentExpression. This is a structural difference from Serilog's
    // source-gen and from Herald's native interpolated-string-handler path.
    [Fact]
    public void Hole_names_come_from_template_not_from_argument_expressions()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<SerilogTemplateIntegrationTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);

        int myVariableWithALongName = 42;
        log.Information("user {UserId} logged in", myVariableWithALongName);

        var events = sink.GetEvents();
        events.Should().HaveCount(1);

        events[0].Properties.Should().Contain(p => p.Name == "UserId",
            "the property name is 'UserId' from the template, NOT the variable name");
        events[0].Properties.Should().NotContain(p => p.Name == "myVariableWithALongName",
            "CallerArgumentExpression is not used on the params-fallback path");

        // Verify the value is correct. GetProperty returns LogProperty? (nullable struct);
        // access .Value on the unwrapped struct via the null-conditional chain.
        var userIdProp = events[0].GetProperty("UserId");
        userIdProp.Should().NotBeNull("the 'UserId' property must be present");
        userIdProp!.Value.Value.Should().Be(42,
            "the value must match the supplied argument");
    }

    // Fewer args than holes: graceful handling required — must not throw.
    // Only the holes that have a matching arg position get bound.
    [Fact]
    public void Fewer_args_than_holes_does_not_throw()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<SerilogTemplateIntegrationTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);

        // Two holes {A} and {B}, only one arg supplied.
        var act = () => log.Information("x {A} y {B}", 1);
        act.Should().NotThrow("count mismatch must not crash — matches Serilog's graceful behaviour");

        var events = sink.GetEvents();
        events.Should().HaveCount(1, "the event must still be captured");

        // Only the first hole gets a bound property; {B} stays unbound.
        // GetProperty returns LogProperty? (nullable struct); unwrap via .Value
        var propA = events[0].GetProperty("A");
        propA.Should().NotBeNull("the first hole is bound to the first arg");
        propA!.Value.Value.Should().Be(1,
            "the first hole is bound to the first arg");
        events[0].GetProperty("B").Should().BeNull(
            "unbound holes produce no property — the adapter stops at Min(holes, args)");
    }

    // Zero args with holes: template has holes but no args supplied. Must not throw.
    [Fact]
    public void Zero_args_with_holes_does_not_throw()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<SerilogTemplateIntegrationTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);

        var act = () => log.Information("user {Id} logged in");
        act.Should().NotThrow("no args supplied for holes must not crash");

        var events = sink.GetEvents();
        events.Should().HaveCount(1);
        // No args → no properties bound.
        events[0].Properties.Should().BeEmpty(
            "holes with no args produce no bound properties");
    }

    // The MessageTemplate field preserves the original template string, including
    // the hole syntax. This is separate from the rendered Message.
    [Fact]
    public void MessageTemplate_field_preserves_original_template_with_holes()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<SerilogTemplateIntegrationTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);

        const string template = "user {UserId} logged in";
        log.Information(template, 42);

        var events = sink.GetEvents();
        events.Should().HaveCount(1);
        events[0].MessageTemplate.Should().Be(template,
            "MessageTemplate must preserve the original template string with holes intact");
        events[0].Message.Should().Be("user 42 logged in",
            "Message must be the rendered form with hole values substituted");
    }
}

#endif
