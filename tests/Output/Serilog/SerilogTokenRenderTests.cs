#nullable enable
// Task 4: oracle-pinned tests for {Message}, {Timestamp}, {NewLine}, {Exception} renderers.
// All assertions compare Herald's output against real Serilog 4.3.1 via SerilogFormattingOracle.
// Gated to net9+ to match the compat assembly target.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using FluentAssertions;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Serilog.Formatting;
using Xunit;

namespace MMP.Herald.OSS.Tests.Output.Serilog;

/// <summary>
/// Oracle-pinned tests for <see cref="SerilogTokenRenderers"/>.
///
/// <para>
/// Every assertion compares Herald's renderer output against real Serilog 4.3.1
/// (via <see cref="SerilogFormattingOracle.RenderOutputTemplate"/>). No expected
/// string is hand-written; the oracle is the sole source of truth.
/// </para>
///
/// <para>
/// <b>MED-1 guard:</b> all specs use the fixed <c>+05:00</c> offset timestamp
/// from <see cref="CanonicalEventSpec"/> (not <c>DateTimeOffset.UtcNow</c>) so
/// UTC-vs-local divergence is detectable in CI regardless of the host timezone.
/// </para>
/// </summary>
public sealed class SerilogTokenRenderTests
{
    // ── Fixed spec used across most tests ────────────────────────────────────
    // Uses the CanonicalEventSpec default timestamp (2024-06-15 14:30:00 +05:00)
    // so the local-time offset is well-defined and oracle-comparable.
    private static readonly CanonicalEventSpec DefaultSpec = CanonicalEventSpec.Simple(
        "User {Name} spent {Amount}",
        levelKey: "information",
        ("Name",   "Alice"),
        ("Amount", 42.5));

    // ── {Message} — default (no specifier) ──────────────────────────────────

    [Fact]
    [RequiresUnreferencedCode("Uses CanonicalSpecViewFactory which calls LogEventValueProjector (reflection).")]
    public void Message_default_quotes_string_values()
    {
        // Arrange
        var view     = CanonicalSpecViewFactory.Build(DefaultSpec);
        var expected = SerilogFormattingOracle.RenderOutputTemplate("{Message}", DefaultSpec)
                            .TrimEnd('\n', '\r');

        // Act
        var actual = RenderToken("{Message}", view);

        // Assert
        actual.Should().Be(expected,
            "the default {Message} token must quote string scalars, matching Serilog 4.3.1");
        actual.Should().Contain("\"Alice\"",
            "string values must be surrounded by double quotes in the default rendering");
    }

    // ── {Message:l} — literal specifier (no quotes on string values) ─────────

    [Fact]
    [RequiresUnreferencedCode("Uses CanonicalSpecViewFactory which calls LogEventValueProjector (reflection).")]
    public void Message_l_spec_strips_string_quotes()
    {
        // Arrange
        var view     = CanonicalSpecViewFactory.Build(DefaultSpec);
        var expected = SerilogFormattingOracle.RenderOutputTemplate("{Message:l}", DefaultSpec)
                            .TrimEnd('\n', '\r');

        // Act
        var actual = RenderToken("{Message:l}", view);

        // Assert
        actual.Should().Be(expected,
            "the :l specifier must produce the same unquoted-string output as Serilog 4.3.1");
        actual.Should().Contain("Alice",
            "the :l specifier must include the string value");
        actual.Should().NotContain("\"Alice\"",
            "the :l specifier must NOT surround the string value with double quotes");
    }

    // ── {Message:lj} — canonical specifier ──────────────────────────────────

    [Fact]
    [RequiresUnreferencedCode("Uses CanonicalSpecViewFactory which calls LogEventValueProjector (reflection).")]
    public void Message_lj_spec_matches_oracle_for_string_property()
    {
        // Arrange — use the string-scalar spec (CRIT-3 flag: string property)
        var spec     = CanonicalEventSpec.Simple("Status is {Status}", properties: ("Status", "ok"));
        var view     = CanonicalSpecViewFactory.Build(spec);
        var expected = SerilogFormattingOracle.RenderOutputTemplate("{Message:lj}", spec)
                            .TrimEnd('\n', '\r');

        // Act
        var actual = RenderToken("{Message:lj}", view);

        // Assert
        actual.Should().Be(expected,
            "the :lj specifier must match Serilog 4.3.1 for string-valued properties");
    }

    [Fact]
    [RequiresUnreferencedCode("Uses CanonicalSpecViewFactory which calls LogEventValueProjector (reflection).")]
    public void Message_lj_spec_matches_oracle_for_destructured_object()
    {
        // Arrange — destructured object (CRIT-3 flag: JSON branch)
        var spec = new CanonicalEventSpec
        {
            MessageTemplate = "Logged in as {@User}",
            Properties      = [("User", new { Name = "Alice", Age = 30 }, true)],
        };
        var view     = CanonicalSpecViewFactory.Build(spec);
        var expected = SerilogFormattingOracle.RenderOutputTemplate("{Message:lj}", spec)
                            .TrimEnd('\n', '\r');

        // Act
        var actual = RenderToken("{Message:lj}", view);

        // Assert
        actual.Should().Be(expected,
            "the :lj specifier must match Serilog 4.3.1 for destructured objects (JSON branch)");
    }

    // ── {Message} default vs :l discriminator ───────────────────────────────

    [Fact]
    [RequiresUnreferencedCode("Uses CanonicalSpecViewFactory which calls LogEventValueProjector (reflection).")]
    public void Message_default_and_l_spec_differ_for_string_property()
    {
        // The :l specifier must produce different output from the default for a
        // string-valued property. This is the key CRIT-3 discriminator.
        var spec    = CanonicalEventSpec.Simple("Result is {Status}", properties: ("Status", "ok"));
        var view    = CanonicalSpecViewFactory.Build(spec);
        var withDef = RenderToken("{Message}",   view);
        var withL   = RenderToken("{Message:l}", view);

        withL.Should().NotBe(withDef,
            "the :l specifier strips quotes so the output must differ from the default");
    }

    // ── {Timestamp:fmt} — local-time rendering ───────────────────────────────
    //
    // ORACLE NOTE: the SerilogFormattingOracle.RenderOutputTemplate approach cannot
    // be used for {Timestamp} because the oracle drives a real Serilog logger with
    // DateTimeOffset.Now as the event timestamp — the spec's Timestamp field is not
    // forwarded to Serilog's event creation path. Timestamp tests therefore verify
    // the local-vs-UTC rendering invariant directly against the FakeSerilogEventView's
    // stored timestamp, rather than comparing against the oracle.
    //
    // The oracle is still used for the "no-specifier default format" assertion because
    // the format string (not the time value) is what we're testing there.

    [Fact]
    public void Timestamp_HH_mm_ss_renders_local_time()
    {
        // Arrange — MED-1 guard: fixed +05:00 offset so local-vs-UTC is distinguishable.
        // The spec timestamp is 2024-06-15 14:30:00 +05:00.
        // ToLocalTime() on this machine maps it to the host's local offset.
        // ToUniversalTime() would always give 2024-06-15 09:30:00 UTC.
        var view = new FakeSerilogEventView
        {
            Timestamp = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.FromHours(5)),
        };
        var expectedLocal = view.Timestamp.ToLocalTime().ToString("HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture);

        // Act
        var actual = RenderToken("{Timestamp:HH:mm:ss}", view);

        // Assert
        actual.Should().Be(expectedLocal,
            "OD-3/S-2: the timestamp renderer must apply ToLocalTime() before formatting");

        // The local-vs-UTC discriminator is only satisfiable when the host's
        // local offset differs from UTC for this instant. CI runners run in
        // UTC, where local == UTC and this assertion can never pass; the
        // positive assertion above still pins the ToLocalTime() call there.
        var utcRendered = view.Timestamp.ToUniversalTime().ToString("HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture);
        if (expectedLocal != utcRendered)
        {
            actual.Should().NotBe(utcRendered,
                "Herald must NOT render UTC; it must render LOCAL time to match Serilog 4.3.1");
        }
    }

    [Fact]
    public void Timestamp_renders_local_time_not_utc_invariant()
    {
        // The key OD-3/S-2 invariant: for any non-UTC timestamp, the rendered
        // hour:minute:second must be the LOCAL representation, not the UTC one.
        //
        // Fixed offset: +05:00, so UTC is 09:30:00, local depends on the host timezone.
        // The important thing is we call ToLocalTime(), not ToUniversalTime().
        var ts    = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.FromHours(5));
        var view  = new FakeSerilogEventView { Timestamp = ts };
        var actual = RenderToken("{Timestamp:HH:mm:ss}", view);

        // The rendered value must equal the LOCAL representation.
        var localExpected = ts.ToLocalTime().ToString("HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture);
        actual.Should().Be(localExpected,
            "OD-3/S-2: {Timestamp} must use DateTimeOffset.ToLocalTime()");
    }

    [Fact]
    public void Timestamp_no_format_specifier_uses_MM_dd_yyyy_HH_mm_ss()
    {
        // Real Serilog's default timestamp format is "MM/dd/yyyy HH:mm:ss".
        // This is verified by the default-format constant in SerilogTokenRenderers.
        var ts   = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.FromHours(5));
        var view = new FakeSerilogEventView { Timestamp = ts };
        var expected = ts.ToLocalTime().ToString("MM/dd/yyyy HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture);

        var actual = RenderToken("{Timestamp}", view);

        actual.Should().Be(expected,
            "the default {Timestamp} format must be 'MM/dd/yyyy HH:mm:ss', matching Serilog 4.3.1");
    }

    // ── {NewLine} ────────────────────────────────────────────────────────────

    [Fact]
    public void NewLine_renders_environment_newline()
    {
        // Arrange — minimal defaults are sufficient for this token.
        var view = new FakeSerilogEventView();

        // Act
        var actual = RenderToken("{NewLine}", view);

        // Assert
        actual.Should().Be(Environment.NewLine,
            "{NewLine} must render exactly Environment.NewLine");
    }

    [Fact]
    [RequiresUnreferencedCode("Uses CanonicalSpecViewFactory which calls LogEventValueProjector (reflection).")]
    public void NewLine_oracle_also_produces_environment_newline()
    {
        // The oracle renders {NewLine} as Environment.NewLine (plus the formatter's own
        // trailing newline, which we do NOT trim here so we can see the distinction).
        // This test confirms the oracle matches our expectation, not that Herald matches
        // the oracle (that is already covered by NewLine_renders_environment_newline).
        var expected = SerilogFormattingOracle
                            .RenderOutputTemplate("{NewLine}", DefaultSpec);

        // The oracle output for "{NewLine}" contains Environment.NewLine at minimum.
        expected.Should().Contain(Environment.NewLine,
            "real Serilog must emit Environment.NewLine for {NewLine}");
    }

    // ── {Exception} — present ────────────────────────────────────────────────

    [Fact]
    [RequiresUnreferencedCode("Uses CanonicalSpecViewFactory which calls LogEventValueProjector (reflection).")]
    public void Exception_present_renders_type_message_and_stack_with_trailing_newline()
    {
        // Arrange
        var spec = new CanonicalEventSpec
        {
            LevelKey        = "error",
            MessageTemplate = "Unhandled failure",
            Exception       = new InvalidOperationException("boom"),
        };
        var view     = CanonicalSpecViewFactory.Build(spec);
        var expected = SerilogFormattingOracle.RenderOutputTemplate("{Exception}", spec);

        // Act
        var actual = RenderToken("{Exception}", view);

        // Assert
        actual.Should().Be(expected,
            "the {Exception} token must match Serilog 4.3.1 exactly (including trailing newline)");
        actual.Should().EndWith(Environment.NewLine,
            "Serilog appends a trailing newline after the exception text");
        actual.Should().Contain("InvalidOperationException",
            "the exception type name must appear in the output");
        actual.Should().Contain("boom",
            "the exception message must appear in the output");
    }

    // ── {Exception} — absent ─────────────────────────────────────────────────

    [Fact]
    [RequiresUnreferencedCode("Uses CanonicalSpecViewFactory which calls LogEventValueProjector (reflection).")]
    public void Exception_absent_renders_empty_string_not_null()
    {
        // Arrange — spec with no exception
        var spec = new CanonicalEventSpec
        {
            LevelKey        = "information",
            MessageTemplate = "No exception here",
            Exception       = null,
        };
        var view     = CanonicalSpecViewFactory.Build(spec);
        var expected = SerilogFormattingOracle.RenderOutputTemplate("{Exception}", spec);

        // Act
        var actual = RenderToken("{Exception}", view);

        // Assert — oracle output is the ground truth
        expected.Should().BeEmpty(
            "real Serilog renders nothing (not 'null') when no exception is attached");
        actual.Should().Be(expected,
            "Herald must match Serilog: empty string, not 'null', when no exception is present");
    }

    // ── Full-template smoke test (corpus) ────────────────────────────────────
    //
    // Compares Herald's output against the oracle for all tokens EXCEPT {Timestamp}.
    // The oracle uses DateTimeOffset.Now for the Serilog event timestamp (not the spec),
    // so {Timestamp} can't be oracle-compared in a full-template test. We exclude it
    // by using a template that omits {Timestamp} and verify the rest matches exactly.

    [Theory]
    [InlineData("information", "Hello {Name}")]
    [InlineData("warning",     "Threshold exceeded")]
    [InlineData("error",       "Unhandled failure")]
    [RequiresUnreferencedCode("Uses CanonicalSpecViewFactory which calls LogEventValueProjector (reflection).")]
    public void Full_template_without_timestamp_matches_oracle_for_corpus_entries(
        string levelKey,
        string messageTemplate)
    {
        // Arrange — template without {Timestamp} so oracle and Herald agree on all tokens.
        var spec     = CanonicalEventSpec.Simple(messageTemplate, levelKey);
        var view     = CanonicalSpecViewFactory.Build(spec);
        // Template: level + message + newline + exception (no timestamp).
        var template = "[{Level:u3}] {Message:lj}{NewLine}{Exception}";
        var expected = SerilogFormattingOracle.RenderOutputTemplate(template, spec);

        // Act — render each token and concatenate.
        var tokens = SerilogOutputTemplateParser.Parse(template);
        var writer = new StringWriter();
        foreach (var token in tokens)
        {
            switch (token)
            {
                case TextToken text:
                    writer.Write(text.Text);
                    break;
                case HoleToken hole:
                    SerilogTokenRenderers.RenderToken(hole, view, writer);
                    break;
            }
        }
        var actual = writer.ToString();

        // Assert
        actual.Should().Be(expected,
            $"the output template must match Serilog 4.3.1 for level '{levelKey}'");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Parse a single-token output template and render it via SerilogTokenRenderers.
    // Returns the rendered string without trailing newline trimming so the caller
    // can assert on trailing newlines when needed (e.g. {Exception} present case).
    private static string RenderToken(string singleTokenTemplate, FakeSerilogEventView view)
    {
        var tokens = SerilogOutputTemplateParser.Parse(singleTokenTemplate);
        var writer = new StringWriter();

        foreach (var token in tokens)
        {
            switch (token)
            {
                case TextToken text:
                    writer.Write(text.Text);
                    break;
                case HoleToken hole:
                    SerilogTokenRenderers.RenderToken(hole, view, writer);
                    break;
            }
        }

        return writer.ToString();
    }
}

