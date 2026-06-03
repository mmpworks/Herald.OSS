#nullable enable
// Renderer tests for the {Properties} output-template token (P3 Task 5).
// Gated to net9+ — MMP.Herald.Serilog targets net9/net10 only.

using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Serilog.Events;
using MMP.Herald.Serilog.Formatting;
using Xunit;

namespace MMP.Herald.OSS.Tests.Output.Serilog;

/// <summary>
/// Oracle-pinned tests for <see cref="SerilogPropertiesRenderer"/>.
///
/// <para>
/// Every assertion compares Herald's renderer output to real Serilog's output via
/// <see cref="SerilogFormattingOracle.RenderOutputTemplate"/>. No expected strings
/// are hand-written — the oracle is the single source of truth.
/// </para>
///
/// <para>
/// <b>Critical rule under test.</b> <c>{Properties}</c> must exclude properties whose
/// names appear as holes in the message template. Failure to exclude them causes
/// silent double-rendering of already-visible message values.
/// </para>
/// </summary>
public sealed class SerilogPropertiesRendererTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    // Drive the renderer against a FakeSerilogEventView and return the output string.
    private static string RunRenderer(
        IReadOnlyDictionary<string, LogEventPropertyValue> allProperties,
        IReadOnlyList<string> templateHoleNames,
        string? format = null)
    {
        var view = new FakeSerilogEventView
        {
            Properties       = allProperties,
            TemplateHoleNames = templateHoleNames,
        };

        var writer = new StringWriter();
        SerilogPropertiesRenderer.Render(view, format, writer);
        return writer.ToString();
    }

    // Build an IReadOnlyDictionary<string, LogEventPropertyValue> from name→value pairs.
    // All values are treated as ScalarValues (the common case for {Properties} tests).
    private static IReadOnlyDictionary<string, LogEventPropertyValue> Props(
        params (string Name, object? Value)[] pairs)
    {
        var dict = new Dictionary<string, LogEventPropertyValue>(StringComparer.Ordinal);
        foreach (var (name, value) in pairs)
            dict[name] = new ScalarValue(value);
        return dict;
    }

    // ── Core exclusion rule ───────────────────────────────────────────────────

    /// <summary>
    /// {Properties} must include only properties NOT named as holes in the
    /// message template. This is the load-bearing correctness rule.
    /// </summary>
    [Fact]
    public void Properties_excludes_message_template_holes()
    {
        // Oracle: real Serilog with UserId+Action bound to the template,
        // RequestId added via context (ForContext).
        var spec = new CanonicalEventSpec
        {
            MessageTemplate  = "User {UserId} did {Action}",
            Properties       = [("UserId", (object?)42, false), ("Action", "purchase", false)],
            ContextProperties = [("RequestId", "abc123")],
        };
        var oracle = SerilogFormattingOracle.RenderOutputTemplate("{Properties}", spec);

        // Structural assertion: oracle must include RequestId but not the template holes.
        oracle.Should().Contain("RequestId",
            "RequestId is not in the message template so {Properties} must show it");
        oracle.Should().NotContain("UserId",
            "UserId is bound to a message-template hole and must be excluded from {Properties}");
        oracle.Should().NotContain("Action",
            "Action is bound to a message-template hole and must be excluded from {Properties}");

        // Herald renderer must match oracle exactly.
        var heraldOutput = RunRenderer(
            allProperties: Props(
                ("UserId", 42),
                ("Action", "purchase"),
                ("RequestId", "abc123")),
            templateHoleNames: ["UserId", "Action"]);

        heraldOutput.Should().Be(oracle,
            "Herald's {Properties} renderer must match real Serilog output exactly");
    }

    /// <summary>
    /// When all event properties are bound to template holes, {Properties} renders
    /// the empty form (no residual).
    /// </summary>
    [Fact]
    public void Properties_when_all_in_template_renders_empty()
    {
        var spec = new CanonicalEventSpec
        {
            MessageTemplate = "User {UserId} did {Action}",
            Properties      = [("UserId", (object?)42, false), ("Action", "purchase", false)],
        };
        var oracle = SerilogFormattingOracle.RenderOutputTemplate("{Properties}", spec);

        var heraldOutput = RunRenderer(
            allProperties: Props(("UserId", 42), ("Action", "purchase")),
            templateHoleNames: ["UserId", "Action"]);

        heraldOutput.Should().Be(oracle,
            "both must render the empty {Properties} form when all properties are template holes");
    }

    /// <summary>
    /// When the message template has no holes, {Properties} renders all event properties.
    /// </summary>
    [Fact]
    public void Properties_when_none_in_template_renders_all()
    {
        var spec = new CanonicalEventSpec
        {
            MessageTemplate   = "Something happened",
            ContextProperties = [("X", (object?)1), ("Y", 2)],
        };
        var oracle = SerilogFormattingOracle.RenderOutputTemplate("{Properties}", spec);

        oracle.Should().Contain("X", "X is not in the template so must appear in {Properties}");
        oracle.Should().Contain("Y", "Y is not in the template so must appear in {Properties}");

        // Serilog's ForContext uses LIFO ordering: last ForContext-added appears first.
        // ContextProperties [("X",1),("Y",2)] → oracle renders Y before X.
        // FakeSerilogEventView.Properties is a plain dict; pass keys in oracle-matching
        // order so the iteration order matches what the oracle produces.
        var heraldOutput = RunRenderer(
            allProperties: Props(("Y", 2), ("X", 1)),
            templateHoleNames: []);

        heraldOutput.Should().Be(oracle,
            "Herald must match oracle when all properties are context-only (none in template)");
    }

    /// <summary>
    /// When the event has no properties at all, {Properties} renders the empty form.
    /// </summary>
    [Fact]
    public void Properties_with_no_properties_renders_empty()
    {
        var spec = new CanonicalEventSpec { MessageTemplate = "ping" };
        var oracle = SerilogFormattingOracle.RenderOutputTemplate("{Properties}", spec);

        var heraldOutput = RunRenderer(
            allProperties: Props(),
            templateHoleNames: []);

        heraldOutput.Should().Be(oracle,
            "no properties at all must yield the same empty form as real Serilog");
    }

    // ── Value formatting ──────────────────────────────────────────────────────

    /// <summary>
    /// String scalar values must be double-quoted in the default format.
    /// </summary>
    [Fact]
    public void Properties_string_value_is_double_quoted()
    {
        var spec = new CanonicalEventSpec
        {
            MessageTemplate   = "Hello",
            ContextProperties = [("Name", (object?)"Alice")],
        };
        var oracle = SerilogFormattingOracle.RenderOutputTemplate("{Properties}", spec);

        oracle.Should().Contain("\"Alice\"",
            "Serilog quotes string values in {Properties} default format");

        var heraldOutput = RunRenderer(
            allProperties: Props(("Name", "Alice")),
            templateHoleNames: []);

        heraldOutput.Should().Be(oracle, "Herald must match oracle string quoting");
    }

    /// <summary>
    /// Integer scalar values must be bare (not quoted) in the default format.
    /// </summary>
    [Fact]
    public void Properties_integer_value_is_bare()
    {
        var spec = new CanonicalEventSpec
        {
            MessageTemplate   = "Hello",
            ContextProperties = [("Count", (object?)42)],
        };
        var oracle = SerilogFormattingOracle.RenderOutputTemplate("{Properties}", spec);

        oracle.Should().Contain("42", "integer values must appear bare in {Properties}");
        oracle.Should().NotContain("\"42\"", "integers must not be quoted");

        var heraldOutput = RunRenderer(
            allProperties: Props(("Count", 42)),
            templateHoleNames: []);

        heraldOutput.Should().Be(oracle, "Herald must match oracle integer rendering");
    }

    /// <summary>
    /// Null values must render as the bare token <c>null</c>.
    /// </summary>
    [Fact]
    public void Properties_null_value_renders_as_null_token()
    {
        var spec = new CanonicalEventSpec
        {
            MessageTemplate   = "Something {X}",
            Properties        = [("X", (object?)1, false)],
            ContextProperties = [("NullProp", (object?)null)],
        };
        var oracle = SerilogFormattingOracle.RenderOutputTemplate("{Properties}", spec);

        oracle.Should().Contain("null", "null values must render as the bare null token");

        var heraldOutput = RunRenderer(
            allProperties: Props(("X", 1), ("NullProp", null)),
            templateHoleNames: ["X"]);

        heraldOutput.Should().Be(oracle, "Herald must match oracle null rendering");
    }

    // ── JSON format specifier ─────────────────────────────────────────────────

    /// <summary>
    /// The <c>:j</c> format specifier renders in JSON style — no surrounding spaces,
    /// keys and string values in double-quotes, without the Serilog default separator.
    /// </summary>
    [Fact]
    public void Properties_format_j_renders_json_style()
    {
        var spec = new CanonicalEventSpec
        {
            MessageTemplate  = "User {UserId} did {Action}",
            Properties       = [("UserId", (object?)42, false), ("Action", "purchase", false)],
            ContextProperties = [("RequestId", "abc123")],
        };
        var oracleJson    = SerilogFormattingOracle.RenderOutputTemplate("{Properties:j}", spec);
        var oracleDefault = SerilogFormattingOracle.RenderOutputTemplate("{Properties}",   spec);

        // JSON format must differ from default format.
        oracleJson.Should().NotBe(oracleDefault,
            ":j format produces different output from the default format");

        // JSON format must contain the key and value without surrounding spaces.
        oracleJson.Should().Contain("\"RequestId\"",
            ":j format must quote the key name");
        oracleJson.Should().Contain("\"abc123\"",
            ":j format must quote string values");

        // Herald :j output must match oracle.
        var heraldJson = RunRenderer(
            allProperties: Props(("UserId", 42), ("Action", "purchase"), ("RequestId", "abc123")),
            templateHoleNames: ["UserId", "Action"],
            format: "j");

        heraldJson.Should().Be(oracleJson,
            "Herald's :j format must match real Serilog output exactly");
    }

    // ── Multiple residual properties ──────────────────────────────────────────

    /// <summary>
    /// Multiple residual properties must be separated by <c>, </c> in the default format.
    /// </summary>
    [Fact]
    public void Properties_multiple_residual_renders_comma_separated()
    {
        var spec = new CanonicalEventSpec
        {
            MessageTemplate   = "User {UserId}",
            Properties        = [("UserId", (object?)1, false)],
            ContextProperties = [("A", "x"), ("B", "y"), ("C", (object?)3)],
        };
        var oracle = SerilogFormattingOracle.RenderOutputTemplate("{Properties}", spec);

        oracle.Should().Contain("A", "A must appear in {Properties}");
        oracle.Should().Contain("B", "B must appear in {Properties}");
        oracle.Should().Contain("C", "C must appear in {Properties}");
        oracle.Should().NotContain("UserId",
            "UserId is a template hole and must be excluded");

        // Serilog LIFO ordering: ContextProperties [A, B, C] → oracle renders C, B, A.
        // Pass all properties to the renderer in oracle-matching LIFO order so the
        // plain-dict iteration order aligns with the oracle output.
        // Template-hole property UserId appears first in the dict; it is excluded by the renderer.
        var heraldOutput = RunRenderer(
            allProperties: Props(("UserId", 1), ("C", 3), ("B", "y"), ("A", "x")),
            templateHoleNames: ["UserId"]);

        heraldOutput.Should().Be(oracle,
            "Herald must match oracle for multiple residual properties");
    }
}

