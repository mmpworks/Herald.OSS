#nullable enable
// Smoke tests for the P3 parity oracle harness (Task 1 Step 3).
// Gated to net9+ to match the compat assembly target.

using FluentAssertions;
using MMP.Herald.OSS.Tests.TestSupport;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog;

/// <summary>
/// Smoke tests that prove the <see cref="SerilogFormattingOracle"/> wiring is correct
/// before it is trusted as the ground truth for P3 Tasks 2–8.
///
/// <para>
/// These tests verify the oracle itself, not Herald's renderer. A failing oracle smoke
/// test means the Serilog package reference or the capture plumbing is broken — not a
/// Herald parity problem.
/// </para>
/// </summary>
public sealed class SerilogFormattingOracleTests
{
    // The simplest possible check: {Message} renders the hole-substituted message.
    // Real Serilog's default rendering quotes string scalar values in the rendered
    // message (e.g. Hello "World"), so we assert on the template prefix + presence
    // of the value, not a bare unquoted form.
    // If this fails the oracle wiring is broken and every downstream parity test is
    // suspect.
    [Fact]
    public void Oracle_renders_simple_message_template()
    {
        var spec = new CanonicalEventSpec
        {
            MessageTemplate = "Hello {Name}",
            Properties      = [("Name", (object?)"World", false)],
        };

        var result = SerilogFormattingOracle.RenderOutputTemplate("{Message}", spec);

        // Serilog's default rendering quotes string values: Hello "World".
        result.Should().Contain("Hello",
            "the oracle must include the static part of the message template");
        result.Should().Contain("World",
            "the oracle must render the bound property value");
    }

    // Verify that {Message:l} (literal — no quotes around string values) differs from
    // {Message} (which quotes strings). This discriminates the :l / :lj specifier branch
    // (CRIT-3 pre-mortem).
    [Fact]
    public void Oracle_renders_message_literal_specifier_strips_string_quotes()
    {
        var spec = CanonicalEventSpec.Simple(
            "Status is {Status}",
            properties: ("Status", "ok"));

        var withLiteral = SerilogFormattingOracle.RenderOutputTemplate("{Message:l}", spec);
        var withDefault = SerilogFormattingOracle.RenderOutputTemplate("{Message}",   spec);

        // :l specifier strips quotes from string values.
        withLiteral.Should().Contain("Status is ok",
            "the :l specifier must produce unquoted string values");

        // Default rendering quotes string scalars in {Message}.
        // The default output contains the value; whether quoted or not is what :l changes.
        withDefault.Should().Contain("Status is",
            "the default {Message} token must render the static prefix");
        withDefault.Should().Contain("ok",
            "the default {Message} token must include the property value");

        // The two specifiers must produce different output for a string-valued property
        // — this is the key discriminator for the :l / :lj compound-specifier tests.
        withLiteral.Should().NotBe(withDefault,
            "the :l specifier changes how string values are rendered");
    }

    // Verify the CLEF oracle produces valid JSON with the @t and @mt fields that
    // Serilog's CompactJsonFormatter always emits.
    [Fact]
    public void Oracle_renders_clef_json_with_required_fields()
    {
        var spec = CanonicalEventSpec.Simple("Ping");

        var clef = SerilogFormattingOracle.RenderClef(spec);

        clef.Should().Contain("\"@t\"",
            "CLEF must carry a timestamp field");
        clef.Should().Contain("\"@mt\"",
            "CLEF must carry a message-template field");
    }

    // Verify that the oracle maps the Warning level correctly (CRIT-1 pre-mortem:
    // Warning→WRN, not WAR). The oracle output is Serilog's ground truth — this test
    // confirms the oracle produces it so Tasks 3+ can compare against it.
    [Fact]
    public void Oracle_renders_warning_level_abbreviated()
    {
        var spec = new CanonicalEventSpec
        {
            LevelKey        = "warning",
            MessageTemplate = "Threshold exceeded",
        };

        var result = SerilogFormattingOracle.RenderOutputTemplate("{Level:u3}", spec);

        result.Should().Be("WRN",
            "real Serilog abbreviates Warning to WRN (not WAR) — oracle must confirm this");
    }

    // Verify that the CanonicalEventSpec.RepresentativeCorpus() can be driven through
    // the oracle without throwing. This proves all corpus entries are oracle-compatible.
    [Fact]
    public void Oracle_handles_all_representative_corpus_entries_without_throwing()
    {
        var corpus = CanonicalEventSpec.RepresentativeCorpus();
        const string template = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

        foreach (var spec in corpus)
        {
            var act = () => SerilogFormattingOracle.RenderOutputTemplate(template, spec);
            act.Should().NotThrow(
                $"the oracle must handle '{spec.MessageTemplate}' at level '{spec.LevelKey}' without throwing");
        }
    }
}

