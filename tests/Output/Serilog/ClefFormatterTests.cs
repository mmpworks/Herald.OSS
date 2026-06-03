#nullable enable
// P3 Task 8: CompactJsonFormatter + RenderedCompactJsonFormatter (CLEF).
// Gated to net9+ — MMP.Herald.Serilog targets net9/net10 only.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Formatting;
using SerilogMirrorLogEvent = MMP.Herald.Serilog.Events.LogEvent;
using SerilogILogger = MMP.Herald.Serilog.ILogger;
using Xunit;

namespace MMP.Herald.OSS.Tests.Output.Serilog;

/// <summary>
/// P3 Task 8 win-condition tests: <see cref="CompactJsonFormatter"/> and
/// <see cref="RenderedCompactJsonFormatter"/> (CLEF — Compact Log Event Format).
///
/// <para>
/// <b>Strategy.</b> Field presence is oracle-verified against real Serilog's
/// <see cref="SerilogFormattingOracle.RenderClef"/>. Values are not byte-identical
/// (R-3 design decision: two independent JSON writers won't agree on exact formatting)
/// but field presence and semantic values are asserted to be equivalent.
/// </para>
///
/// <para>
/// <b>CLEF field set.</b>
/// <list type="bullet">
///   <item><c>@t</c> — ISO-8601 UTC timestamp (always present).</item>
///   <item><c>@mt</c> — message template (always present).</item>
///   <item><c>@m</c> — rendered message (<see cref="RenderedCompactJsonFormatter"/> only).</item>
///   <item><c>@l</c> — level (omitted for Information — Serilog convention).</item>
///   <item><c>@x</c> — exception (omitted when absent).</item>
///   <item>All event properties as top-level fields.</item>
/// </list>
/// </para>
/// </summary>
public sealed class ClefFormatterTests
{
    // ── CompactJsonFormatter: required field presence ─────────────────────────

    /// <summary>
    /// CompactJsonFormatter produces @t and @mt for every event — the minimum required CLEF fields.
    /// </summary>
    [Fact]
    public void CompactJsonFormatter_contains_timestamp_and_message_template_fields()
    {
        var spec = CanonicalEventSpec.Simple("Ping");
        var json = FormatClef(spec);

        json.Should().Contain("\"@t\"",  "CLEF must carry a timestamp field");
        json.Should().Contain("\"@mt\"", "CLEF must carry a message-template field");
    }

    /// <summary>
    /// @l is present for Warning and omitted for Information (Serilog convention).
    /// </summary>
    [Theory]
    [InlineData("warning",     true)]
    [InlineData("error",       true)]
    [InlineData("fatal",       true)]
    [InlineData("debug",       true)]
    [InlineData("verbose",     true)]
    [InlineData("information", false)]
    public void Level_field_presence_follows_serilog_convention(string levelKey, bool shouldBePresent)
    {
        var spec = CanonicalEventSpec.Simple("Ping", levelKey);
        var json = FormatClef(spec);

        if (shouldBePresent)
            json.Should().Contain("\"@l\"",
                $"@l must be present for {levelKey} level (non-Information convention)");
        else
            json.Should().NotContain("\"@l\"",
                "Serilog omits @l for Information level (well-known convention)");
    }

    /// <summary>
    /// @x is present when the event has an exception and absent when it does not.
    /// </summary>
    [Fact]
    public void Exception_field_present_when_exception_attached()
    {
        var spec = new CanonicalEventSpec
        {
            LevelKey        = "error",
            MessageTemplate = "Boom",
            Exception       = new InvalidOperationException("kaboom"),
        };
        var json = FormatClef(spec);

        json.Should().Contain("\"@x\"",
            "@x must be present when the event carries an exception");
    }

    [Fact]
    public void Exception_field_absent_when_no_exception()
    {
        var spec = CanonicalEventSpec.Simple("No exception here");
        var json = FormatClef(spec);

        json.Should().NotContain("\"@x\"",
            "@x must be omitted when there is no exception attached to the event");
    }

    /// <summary>
    /// Event properties appear as top-level fields (not nested under a "properties" key).
    /// </summary>
    [Fact]
    public void Properties_emitted_as_top_level_fields()
    {
        var spec = new CanonicalEventSpec
        {
            MessageTemplate = "User {Name} logged in",
            Properties      = [("Name", (object?)"Alice", false)],
        };
        var json = FormatClef(spec);

        json.Should().Contain("\"Name\"",
            "properties must appear as top-level CLEF fields (not nested under 'properties')");
        json.Should().Contain("Alice",
            "the property value must be present in the CLEF output");
    }

    // ── @l value matches Serilog's CLEF level moniker ─────────────────────────

    [Theory]
    [InlineData("warning", "Warning")]
    [InlineData("error",   "Error")]
    [InlineData("fatal",   "Fatal")]
    [InlineData("debug",   "Debug")]
    [InlineData("verbose", "Verbose")]
    public void Level_field_value_matches_serilog_clef_moniker(string levelKey, string expectedMoniker)
    {
        var spec = CanonicalEventSpec.Simple("Ping", levelKey);
        var json = FormatClef(spec);

        json.Should().Contain($"\"@l\":\"{expectedMoniker}\"",
            $"Serilog CLEF uses '{expectedMoniker}' for level '{levelKey}'");
    }

    // ── @t is UTC ISO-8601 ─────────────────────────────────────────────────────

    [Fact]
    public void Timestamp_field_is_parseable_as_utc_iso8601()
    {
        var spec = CanonicalEventSpec.Simple("Ping");
        var json = FormatClef(spec);

        using var doc = JsonDocument.Parse(json.TrimEnd('\r', '\n'));
        doc.RootElement.TryGetProperty("@t", out var tProp).Should().BeTrue("@t must be present");

        DateTimeOffset.TryParse(tProp.GetString(), out _).Should()
            .BeTrue("@t must be a parseable ISO-8601 timestamp string");
    }

    // ── RenderedCompactJsonFormatter: adds @m ─────────────────────────────────

    /// <summary>
    /// RenderedCompactJsonFormatter adds @m (rendered message) on top of the base CLEF set.
    /// </summary>
    [Fact]
    public void RenderedCompactJsonFormatter_adds_rendered_message_field()
    {
        var spec = new CanonicalEventSpec
        {
            MessageTemplate = "Hello {Name}",
            Properties      = [("Name", (object?)"World", false)],
        };
        var json = FormatRenderedClef(spec);

        json.Should().Contain("\"@m\"",
            "RenderedCompactJsonFormatter must add the @m rendered-message field");
        json.Should().Contain("Hello",
            "the @m field must contain the rendered message prefix");
        json.Should().Contain("World",
            "the @m field must include the substituted property value");
    }

    [Fact]
    public void CompactJsonFormatter_does_not_emit_rendered_message_field()
    {
        var spec = CanonicalEventSpec.Simple("Ping");
        var json = FormatClef(spec);

        json.Should().NotContain("\"@m\"",
            "CompactJsonFormatter must NOT include the @m field (only RenderedCompactJsonFormatter does)");
    }

    [Fact]
    public void RenderedCompactJsonFormatter_also_contains_base_clef_fields()
    {
        var spec = CanonicalEventSpec.Simple("Ping");
        var json = FormatRenderedClef(spec);

        json.Should().Contain("\"@t\"",  "RenderedCompactJsonFormatter must include @t");
        json.Should().Contain("\"@mt\"", "RenderedCompactJsonFormatter must include @mt");
    }

    // ── Output is valid JSON ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]  // simple scalar
    [InlineData(1)]  // integer scalar
    [InlineData(2)]  // string-valued property
    [InlineData(4)]  // warning level
    [InlineData(5)]  // fatal level
    [InlineData(6)]  // event with exception
    public void CompactJsonFormatter_output_is_valid_json(int corpusIndex)
    {
        var corpus = CanonicalEventSpec.RepresentativeCorpus();
        corpusIndex.Should().BeLessThan(corpus.Count, "corpus index out of range");

        var json = FormatClef(corpus[corpusIndex]).TrimEnd('\r', '\n');

        // Must parse without throwing.
        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow($"CLEF output for corpus[{corpusIndex}] must be valid JSON");
    }

    // ── G-GAP.5 oracle parity: field presence matches real Serilog ────────────

    /// <summary>
    /// Field presence must match real Serilog's CompactJsonFormatter for each
    /// representative corpus entry (G-GAP.5 parity requirement).
    ///
    /// <para>
    /// Comparison is field-set equivalence (not byte-identical) because two
    /// independent JSON writers differ in whitespace, key order, and number
    /// formatting (R-3 design decision).
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(0)]  // simple scalar
    [InlineData(1)]  // integer scalar
    [InlineData(2)]  // string-valued property
    [InlineData(4)]  // warning level — @l present
    [InlineData(5)]  // fatal level — @l present
    [InlineData(6)]  // event with exception — @x present
    public void Field_presence_matches_real_serilog_oracle(int corpusIndex)
    {
        var corpus = CanonicalEventSpec.RepresentativeCorpus();
        corpusIndex.Should().BeLessThan(corpus.Count, "corpus index out of range");

        var spec = corpus[corpusIndex];

        // Real Serilog reference output.
        var oracleJson = SerilogFormattingOracle.RenderClef(spec).TrimEnd('\r', '\n');
        var expectedKeys = JsonDocument.Parse(oracleJson)
            .RootElement.EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet();

        // Herald CLEF output.
        var actualJson = FormatClef(spec).TrimEnd('\r', '\n');
        var actualKeys = JsonDocument.Parse(actualJson)
            .RootElement.EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet();

        actualKeys.Should().BeEquivalentTo(expectedKeys,
            $"corpus[{corpusIndex}] (level={spec.LevelKey}, template='{spec.MessageTemplate}') " +
            "field presence must match real Serilog CompactJsonFormatter");
    }

    // ── CLEF line format ──────────────────────────────────────────────────────

    [Fact]
    public void CompactJsonFormatter_output_ends_with_newline()
    {
        var spec = CanonicalEventSpec.Simple("Ping");
        var raw  = FormatClefRaw(spec);

        raw.Should().EndWith(Environment.NewLine,
            "CLEF format requires one JSON object per line (newline-delimited)");
    }

    [Fact]
    public void CompactJsonFormatter_output_is_single_json_object_line()
    {
        var spec = CanonicalEventSpec.Simple("Ping");
        var raw  = FormatClefRaw(spec);

        // A single CLEF line is one JSON object followed by a newline.
        var trimmed = raw.TrimEnd('\r', '\n');
        trimmed.Should().StartWith("{", "CLEF line must start with a JSON object");
        trimmed.Should().EndWith("}",   "CLEF line must end with a JSON object close");
    }

    // ── Null-argument guards ──────────────────────────────────────────────────

    [Fact]
    public void CompactJsonFormatter_throws_on_null_logEvent()
    {
        var formatter = new CompactJsonFormatter();
        var writer    = new StringWriter();

        var act = () => formatter.Format(null!, writer);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logEvent");
    }

    [Fact]
    public void CompactJsonFormatter_throws_on_null_output()
    {
        var formatter = new CompactJsonFormatter();
        var logEvent  = BuildLogEvent(CanonicalEventSpec.Simple("Ping"));

        var act = () => formatter.Format(logEvent, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("output");
    }

    [Fact]
    public void RenderedCompactJsonFormatter_throws_on_null_logEvent()
    {
        var formatter = new RenderedCompactJsonFormatter();
        var writer    = new StringWriter();

        var act = () => formatter.Format(null!, writer);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logEvent");
    }

    [Fact]
    public void RenderedCompactJsonFormatter_throws_on_null_output()
    {
        var formatter = new RenderedCompactJsonFormatter();
        var logEvent  = BuildLogEvent(CanonicalEventSpec.Simple("Ping"));

        var act = () => formatter.Format(logEvent, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("output");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Format using CompactJsonFormatter and return the trimmed JSON string.
    private static string FormatClef(CanonicalEventSpec spec)
        => FormatClefRaw(spec).TrimEnd('\r', '\n');

    // Format using CompactJsonFormatter and return the raw output (with trailing newline).
    private static string FormatClefRaw(CanonicalEventSpec spec)
    {
        var logEvent  = BuildLogEvent(spec);
        var formatter = new CompactJsonFormatter();
        var writer    = new StringWriter();
        formatter.Format(logEvent, writer);
        return writer.ToString();
    }

    // Format using RenderedCompactJsonFormatter and return the trimmed JSON string.
    private static string FormatRenderedClef(CanonicalEventSpec spec)
    {
        var logEvent  = BuildLogEvent(spec);
        var formatter = new RenderedCompactJsonFormatter();
        var writer    = new StringWriter();
        formatter.Format(logEvent, writer);
        return writer.ToString().TrimEnd('\r', '\n');
    }

    // Build a MMP.Herald.Serilog.Events.LogEvent (P1 mirror) from a CanonicalEventSpec
    // using the same pattern as MessageTemplateTextFormatterTests.BuildLogEvent.
    private static SerilogMirrorLogEvent BuildLogEvent(CanonicalEventSpec spec)
    {
        var (herald, sink) = TestLoggers.CreateCapturing<ClefFormatterTests>();
        SerilogILogger adapter = new SerilogLoggerAdapter(herald);

        var args = new object?[spec.Properties.Count];
        for (var i = 0; i < spec.Properties.Count; i++)
            args[i] = spec.Properties[i].Value;

        switch (spec.LevelKey)
        {
            case "verbose":
                if (spec.Exception is { } vex) adapter.Verbose(vex, spec.MessageTemplate, args);
                else                            adapter.Verbose(spec.MessageTemplate, args);
                break;
            case "debug":
                if (spec.Exception is { } dex) adapter.Debug(dex, spec.MessageTemplate, args);
                else                           adapter.Debug(spec.MessageTemplate, args);
                break;
            case "warning":
                if (spec.Exception is { } wex) adapter.Warning(wex, spec.MessageTemplate, args);
                else                           adapter.Warning(spec.MessageTemplate, args);
                break;
            case "error":
                if (spec.Exception is { } eex) adapter.Error(eex, spec.MessageTemplate, args);
                else                           adapter.Error(spec.MessageTemplate, args);
                break;
            case "fatal":
                if (spec.Exception is { } fex) adapter.Fatal(fex, spec.MessageTemplate, args);
                else                           adapter.Fatal(spec.MessageTemplate, args);
                break;
            default: // information + unknown
                if (spec.Exception is { } iex) adapter.Information(iex, spec.MessageTemplate, args);
                else                           adapter.Information(spec.MessageTemplate, args);
                break;
        }

        var native = sink.GetEvents();
        native.Should().HaveCount(1, "exactly one event must be captured for CLEF formatting");

        return new SerilogMirrorLogEvent(native[0]);
    }
}

