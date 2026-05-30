#nullable enable
#if NET9_0_OR_GREATER

using FluentAssertions;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Serilog;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog;

/// <summary>
/// ForContext scope-mapping tests.
///
/// Design facts (read before modifying):
/// - ForContext-injected properties go to LogEvent.Context (IReadOnlyDictionary{string, object?}),
///   NOT to LogEvent.Properties (template holes). Context values are stored as raw object?.
/// - SerilogLoggerAdapter.ForContext(string, object?, bool) ignores the destructureObjects flag
///   at the context level. The value is stored as-is; CaptureMode is NOT tracked in the context
///   dictionary. This is a known gap — context carries raw values only, not capture semantics.
/// - StructuredLogger.ForContext returns a new logger with a merged copy of the context.
///   The parent's context dict is never mutated in place.
/// - ForContext{T}() sets SourceContext to type.FullName (the full qualified name), matching
///   Serilog's convention.
/// </summary>
public sealed class SerilogForContextTests
{
    // ForContext-injected properties appear in LogEvent.Context, not .Properties.
    [Fact]
    public void ForContext_adds_property_to_Context_dictionary()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<SerilogForContextTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);

        var child = log.ForContext("Service", "payments");
        child.Information("request received");

        var events = sink.GetEvents();
        events.Should().HaveCount(1);
        events[0].Context.Should().ContainKey("Service");
        events[0].Context["Service"].Should().Be("payments");
    }

    // After ForContext returns a child, logging through the original parent must
    // NOT carry the child's property. Catches accidental shared/mutable context.
    [Fact]
    public void ForContext_does_not_mutate_the_parent_logger()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<SerilogForContextTests>();
        MMP.Herald.Serilog.ILogger parent = new SerilogLoggerAdapter(herald);

        // Obtain child but log through PARENT only.
        var _ = parent.ForContext("ChildProp", "value");
        parent.Information("parent event");

        var events = sink.GetEvents();
        events.Should().HaveCount(1);
        events[0].Context.Should().NotContainKey("ChildProp",
            "ForContext returns a NEW logger sharing a copy of the context; " +
            "the parent's context must be unaffected");
    }

    // Chaining ForContext calls merges properties: the grandchild carries A AND B.
    [Fact]
    public void ForContext_chaining_accumulates_properties()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<SerilogForContextTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);

        var grandchild = log.ForContext("A", 1).ForContext("B", 2);
        grandchild.Information("chained event");

        var events = sink.GetEvents();
        events.Should().HaveCount(1);
        events[0].Context.Should().ContainKey("A")
            .And.ContainKey("B",
                "chaining ForContext must accumulate, not overwrite");
        events[0].Context["A"].Should().Be(1);
        events[0].Context["B"].Should().Be(2);
    }

    // destructureObjects: true — the value IS stored in Context. CaptureMode is NOT
    // tracked at the context level (context stores raw object? only). This test pins
    // the current behaviour: the key is present with the raw value; callers that need
    // Destructure semantics on context values must use template holes with {@...} syntax.
    [Fact]
    public void ForContext_with_destructureObjects_true_stores_value_as_raw_object_in_context()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<SerilogForContextTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);

        var order = new { Sku = "A1", Qty = 2 };
        var child = log.ForContext("Order", order, destructureObjects: true);
        child.Information("order event");

        var events = sink.GetEvents();
        events.Should().HaveCount(1);

        // Context carries the raw object reference — destructureObjects only affects
        // how the value would be serialised at sink time, which is outside Herald's
        // context dictionary. The adapter does not track capture mode in the context.
        events[0].Context.Should().ContainKey("Order",
            "ForContext with destructureObjects:true must still store the value in Context");
        events[0].Context["Order"].Should().BeSameAs(order,
            "the context stores the original object reference, not a destructured form");
    }

    // ForContext{T}() sets SourceContext to the full qualified type name, matching
    // Serilog's convention: "Namespace.ClassName", not just "ClassName".
    [Fact]
    public void ForContext_generic_sets_SourceContext_to_full_type_name()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<SerilogForContextTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);

        var typed = log.ForContext<SerilogForContextTests>();
        typed.Information("typed source event");

        var events = sink.GetEvents();
        events.Should().HaveCount(1);

        var expected = typeof(SerilogForContextTests).FullName;
        expected.Should().NotBeNull("FullName is non-null for a concrete named type");

        events[0].Context.Should().ContainKey("SourceContext",
            "ForContext<T>() must inject SourceContext into the event context");
        events[0].Context["SourceContext"].Should().Be(expected,
            "SourceContext must be the FULL type name (namespace.type), not just the simple name — " +
            $"expected '{expected}'");
    }

    // ForContext{T}() uses the full name, not just the short name.
    // This is the negative companion to the positive assertion above:
    // the simple name "SerilogForContextTests" must NOT appear as SourceContext.
    [Fact]
    public void ForContext_generic_SourceContext_is_not_just_the_short_type_name()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<SerilogForContextTests>();
        MMP.Herald.Serilog.ILogger log = new SerilogLoggerAdapter(herald);

        log.ForContext<SerilogForContextTests>().Information("event");

        var events = sink.GetEvents();
        events[0].Context.TryGetValue("SourceContext", out var rawSourceContext);
        var sourceContext = rawSourceContext as string;
        sourceContext.Should().NotBe(typeof(SerilogForContextTests).Name,
            "SourceContext must be the FULL name including namespace, not just 'SerilogForContextTests'");
        sourceContext.Should().Contain(".",
            "a full type name includes at least one namespace separator");
    }
}

#endif
