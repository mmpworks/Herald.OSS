#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Pipeline.Processors;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Events;

/// <summary>
/// Regression: <see cref="LogEvent"/> caches a property-name index on the first
/// <see cref="LogEvent.GetProperty"/> call. A <c>with</c> copy must not carry that
/// cache, or the copy answers <c>GetProperty</c> from the ORIGINAL properties.
/// For a redacted copy that returns the raw, unredacted value.
/// </summary>
public sealed class LogEventWithCopyIndexTests
{
    private static LogEvent Event(params LogProperty[] properties) =>
        new(DateTimeOffset.UnixEpoch,
            new LogLevel("information", "Information"),
            new LogCategory("audit.test"),
            "{ssn}",
            "message",
            properties,
            LogEvent.EmptyContext);

    [Fact]
    public void With_copy_answers_get_property_from_its_own_properties()
    {
        var original = Event(new LogProperty("ssn", "999-12-3456"));
        _ = original.GetProperty("ssn"); // builds the cached index

        var copy = original with { Properties = new[] { new LogProperty("ssn", "[REDACTED]") } };

        copy.GetProperty("ssn")!.Value.ResolvedValue.Should().Be("[REDACTED]");
        copy.HasProperty("ssn").Should().BeTrue();
    }

    [Fact]
    public void With_copy_that_removes_a_property_no_longer_reports_it()
    {
        var original = Event(new LogProperty("ssn", "999-12-3456"), new LogProperty("keep", 1));
        _ = original.HasProperty("ssn");

        var copy = original with { Properties = new[] { new LogProperty("keep", 1) } };

        copy.HasProperty("ssn").Should().BeFalse();
        copy.GetProperty("ssn").Should().BeNull();
    }

    [Fact]
    public void Compiled_redaction_after_an_upstream_lookup_hides_the_raw_value_from_get_property()
    {
        // A When predicate reads a property, which builds the index on the input event.
        var processor = new CompiledRedactionProcessor(new List<CompiledRedactionRule>
        {
            new("ssn", RedactionMode.Remove, When: e => e.GetProperty("ssn") is not null),
        });
        var input = Event(new LogProperty("ssn", "999-12-3456"));

        var output = processor.Process(input)!;

        output.Properties[0].ResolvedValue.Should().Be("[REDACTED]");
        output.GetProperty("ssn")!.Value.ResolvedValue.Should().Be("[REDACTED]");
    }

    [Fact]
    public void Index_cache_does_not_take_part_in_equality()
    {
        var properties = new[] { new LogProperty("ssn", "x") };
        var indexed = Event(properties);
        var fresh = indexed with { };
        _ = indexed.GetProperty("ssn");

        fresh.Should().Be(indexed);
        fresh.GetHashCode().Should().Be(indexed.GetHashCode());
    }

    [Fact]
    public void With_copy_keeps_every_positional_value()
    {
        var original = new LogEvent(
            DateTimeOffset.UnixEpoch.AddHours(1),
            new LogLevel("warning", "Warning"),
            new LogCategory("cat"),
            "template {a}",
            "message",
            new[] { new LogProperty("a", 1) },
            new Dictionary<string, object?> { ["k"] = "v" },
            new LogEventId(7, "seven"),
            CausedBy: "cause",
            GenSource: "gen",
            TenantId: "tenant");

        var copy = original with { };

        copy.Should().Be(original);
        copy.TimeUtc.Should().Be(original.TimeUtc);
        copy.Level.Should().Be(original.Level);
        copy.Category.Should().Be(original.Category);
        copy.MessageTemplate.Should().Be(original.MessageTemplate);
        copy.Message.Should().Be(original.Message);
        copy.Properties.Should().BeSameAs(original.Properties);
        copy.Context.Should().BeSameAs(original.Context);
        copy.EventId.Should().Be(original.EventId);
        copy.CausedBy.Should().Be("cause");
        copy.GenSource.Should().Be("gen");
        copy.TenantId.Should().Be("tenant");
    }

    [Fact]
    public void Original_event_keeps_its_own_index_after_a_copy()
    {
        var original = Event(new LogProperty("ssn", "999-12-3456"));
        _ = original.GetProperty("ssn");
        var copy = original with { Properties = new[] { new LogProperty("ssn", "[REDACTED]") } };
        _ = copy.GetProperty("ssn");

        original.GetProperty("ssn")!.Value.ResolvedValue.Should().Be("999-12-3456");
    }
}
