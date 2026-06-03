#nullable enable

using System.IO;
using FluentAssertions;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Serilog.Formatting;
using Serilog.Events;
using Serilog.Formatting.Display;
using Xunit;

namespace MMP.Herald.OSS.Tests.Output.Serilog;

/// <summary>
/// Tests for <see cref="SerilogLevelMoniker.Render"/>.
///
/// <para>
/// Every row in the 6-level core table is oracle-pinned against real Serilog 4.3.1
/// via <c>Matches_real_serilog_output</c>. The moniker assertions and oracle assertions
/// are kept separate so a failure points unambiguously at either Herald or the expected
/// value.
/// </para>
///
/// <para>
/// Herald-extra levels (notice/success/security/metric) have no Serilog counterpart;
/// their behaviour is covered by the <c>Extra_levels_*</c> suite and documented as a
/// pinned gap in the P3 parity audit.
/// </para>
/// </summary>
public sealed class SerilogLevelMonikerTests
{
    // ── Oracle cross-check — real Serilog 4.3.1 ──────────────────────────────
    //
    // These tests run EACH level through real Serilog's MessageTemplateTextFormatter
    // with the "{Level:u3}" output template and assert that SerilogLevelMoniker.Render
    // produces the identical string.
    //
    // How the oracle works: RenderOutputTemplate builds a real LogEvent at the given
    // level then formats it with the supplied output template.  The rendered line
    // is the literal formatter output (e.g. "INF\n").  We trim the trailing newline
    // that MessageTemplateTextFormatter appends by default.
    //
    // If this test suite fails because the oracle returns a different abbreviation,
    // the fix is in SerilogLevelMoniker._canonicalAbbreviationTable — not in the test.

    [Theory]
    [InlineData("verbose",     "u3", "VRB")]
    [InlineData("debug",       "u3", "DBG")]
    [InlineData("information", "u3", "INF")]
    [InlineData("warning",     "u3", "WRN")]
    [InlineData("error",       "u3", "ERR")]
    [InlineData("fatal",       "u3", "FTL")]
    public void Matches_real_serilog_output_u3(string levelKey, string spec, string expected)
    {
        // Oracle: drive real Serilog with "{Level:u3}" output template.
        var oracleOutput = RenderLevelToken(levelKey, $"{{Level:{spec}}}");

        // Pin: the expected table must agree with the oracle.
        oracleOutput.Should().Be(expected,
            $"oracle: real Serilog 4.3.1 renders {levelKey} as '{oracleOutput}' with :{spec}, " +
            "not the expected value — update the expected column, not the oracle.");

        // Moniker: Herald must agree with the oracle.
        SerilogLevelMoniker.Render(levelKey, spec).Should().Be(oracleOutput,
            $"SerilogLevelMoniker.Render({levelKey}, {spec}) must match real Serilog output");
    }

    [Theory]
    [InlineData("verbose",     "w", "verbose")]
    [InlineData("debug",       "w", "debug")]
    [InlineData("information", "w", "information")]
    [InlineData("warning",     "w", "warning")]
    [InlineData("error",       "w", "error")]
    [InlineData("fatal",       "w", "fatal")]
    public void Matches_real_serilog_output_w(string levelKey, string spec, string expected)
    {
        var oracleOutput = RenderLevelToken(levelKey, $"{{Level:{spec}}}");

        oracleOutput.Should().Be(expected,
            $"oracle mismatch for {levelKey}:{spec}");

        SerilogLevelMoniker.Render(levelKey, spec).Should().Be(oracleOutput,
            $"SerilogLevelMoniker.Render({levelKey}, {spec}) must match real Serilog output");
    }

    [Theory]
    [InlineData("verbose",     "u", "VERBOSE")]
    [InlineData("debug",       "u", "DEBUG")]
    [InlineData("information", "u", "INFORMATION")]
    [InlineData("warning",     "u", "WARNING")]
    [InlineData("error",       "u", "ERROR")]
    [InlineData("fatal",       "u", "FATAL")]
    public void Matches_real_serilog_output_u(string levelKey, string spec, string expected)
    {
        var oracleOutput = RenderLevelToken(levelKey, $"{{Level:{spec}}}");

        oracleOutput.Should().Be(expected,
            $"oracle mismatch for {levelKey}:{spec}");

        SerilogLevelMoniker.Render(levelKey, spec).Should().Be(oracleOutput,
            $"SerilogLevelMoniker.Render({levelKey}, {spec}) must match real Serilog output");
    }

    // ── Default (null / empty format) — title-case ───────────────────────────

    [Theory]
    [InlineData("verbose",     null, "Verbose")]
    [InlineData("debug",       null, "Debug")]
    [InlineData("information", null, "Information")]
    [InlineData("warning",     null, "Warning")]
    [InlineData("error",       null, "Error")]
    [InlineData("fatal",       null, "Fatal")]
    public void Default_is_titlecase(string levelKey, string? spec, string expected)
    {
        // Verify the oracle agrees that the default (no specifier) produces title-case.
        var oracleOutput = RenderLevelToken(levelKey, "{Level}");
        oracleOutput.Should().Be(expected,
            $"real Serilog 4.3.1 default {levelKey} must be title-case");

        SerilogLevelMoniker.Render(levelKey, spec).Should().Be(expected);
    }

    [Theory]
    [InlineData("verbose",     "")]
    [InlineData("information", "")]
    [InlineData("warning",     "")]
    public void Empty_format_is_titlecase(string levelKey, string format)
        => SerilogLevelMoniker.Render(levelKey, format)
                              .Should().Be(Titlecase(levelKey));

    // ── Abbreviation table — all six standard levels ──────────────────────────

    [Theory]
    [InlineData("verbose",     "u3", "VRB")]
    [InlineData("debug",       "u3", "DBG")]
    [InlineData("information", "u3", "INF")]
    [InlineData("warning",     "u3", "WRN")]
    [InlineData("error",       "u3", "ERR")]
    [InlineData("fatal",       "u3", "FTL")]
    public void Abbreviation_matches_expected_table(string levelKey, string spec, string expected)
        => SerilogLevelMoniker.Render(levelKey, spec).Should().Be(expected);

    // ── Lowercase full name (:w) ──────────────────────────────────────────────

    [Theory]
    [InlineData("information", "w", "information")]
    [InlineData("warning",     "w", "warning")]
    [InlineData("error",       "w", "error")]
    [InlineData("fatal",       "w", "fatal")]
    [InlineData("verbose",     "w", "verbose")]
    [InlineData("debug",       "w", "debug")]
    public void w_spec_is_lowercase_full_name(string levelKey, string spec, string expected)
        => SerilogLevelMoniker.Render(levelKey, spec).Should().Be(expected);

    // ── Uppercase full name (:u) ──────────────────────────────────────────────

    [Theory]
    [InlineData("information", "u", "INFORMATION")]
    [InlineData("warning",     "u", "WARNING")]
    [InlineData("verbose",     "u", "VERBOSE")]
    [InlineData("debug",       "u", "DEBUG")]
    [InlineData("error",       "u", "ERROR")]
    [InlineData("fatal",       "u", "FATAL")]
    public void u_spec_is_uppercase_full_name(string levelKey, string spec, string expected)
        => SerilogLevelMoniker.Render(levelKey, spec).Should().Be(expected);

    // ── Abbreviated widths 1–5 (oracle-pinned) ───────────────────────────────
    //
    // These values were discovered by running SerilogLevelAbbreviationProbeTests against
    // real Serilog 4.3.1.  Serilog does NOT use simple string truncation — it has per-level
    // canonical tokens at each width (e.g. warning:u2 = "WN", not "WA"; fatal:u4 = "FATL",
    // not "FATA"; debug:u4 = "DBUG", not "DEBU").
    //
    // Each row here BOTH:
    //   1. Asserts the oracle agrees (catches Serilog version drift), and
    //   2. Asserts Herald's moniker matches the oracle.

    [Theory]
    // width 1 — single char, consistently first letter
    [InlineData("verbose",     "u1", "V")]
    [InlineData("debug",       "u1", "D")]
    [InlineData("information", "u1", "I")]
    [InlineData("warning",     "u1", "W")]
    [InlineData("error",       "u1", "E")]
    [InlineData("fatal",       "u1", "F")]
    // width 2 — oracle-verified (warning="WN" not "WA"; verbose="VB"; debug="DE")
    [InlineData("verbose",     "u2", "VB")]
    [InlineData("debug",       "u2", "DE")]
    [InlineData("information", "u2", "IN")]
    [InlineData("warning",     "u2", "WN")]
    [InlineData("error",       "u2", "ER")]
    [InlineData("fatal",       "u2", "FA")]
    // width 4 — oracle-verified (fatal="FATL" not "FATA"; debug="DBUG"; error="EROR")
    [InlineData("verbose",     "u4", "VERB")]
    [InlineData("debug",       "u4", "DBUG")]
    [InlineData("information", "u4", "INFO")]
    [InlineData("warning",     "u4", "WARN")]
    [InlineData("error",       "u4", "EROR")]
    [InlineData("fatal",       "u4", "FATL")]
    // width 5 — oracle-verified
    [InlineData("verbose",     "u5", "VERBO")]
    [InlineData("debug",       "u5", "DEBUG")]
    [InlineData("information", "u5", "INFOR")]
    [InlineData("warning",     "u5", "WARNI")]
    [InlineData("error",       "u5", "ERROR")]
    [InlineData("fatal",       "u5", "FATAL")]
    public void Non_3_widths_match_oracle(string levelKey, string spec, string expected)
    {
        // Oracle cross-check: confirm real Serilog 4.3.1 still produces the expected value.
        var oracleOutput = RenderLevelToken(levelKey, $"{{Level:{spec}}}");
        oracleOutput.Should().Be(expected,
            $"oracle: real Serilog 4.3.1 renders {levelKey}:{spec} as '{oracleOutput}', " +
            $"not '{expected}' — update the expected column (not the oracle) if Serilog changed.");

        // Herald moniker must match the oracle.
        SerilogLevelMoniker.Render(levelKey, spec).Should().Be(oracleOutput,
            $"SerilogLevelMoniker.Render({levelKey}, {spec}) must match real Serilog output");
    }

    // ── Herald extra levels — deterministic fallback ──────────────────────────
    //
    // No Serilog equivalent exists.  The fallback is documented in the P3 parity
    // audit as a pinned gap.  The contract: consistent length, uppercase-only chars.

    [Theory]
    [InlineData("notice",   "u3")]
    [InlineData("success",  "u3")]
    [InlineData("security", "u3")]
    [InlineData("metric",   "u3")]
    public void Extra_levels_return_deterministic_3char_fallback(string levelKey, string spec)
    {
        var result = SerilogLevelMoniker.Render(levelKey, spec);
        result.Should().HaveLength(3, "extras must produce a consistent 3-char fallback");
        result.Should().MatchRegex("^[A-Z]{3}$", "fallback must be 3 uppercase chars");
    }

    [Theory]
    [InlineData("notice",   "w")]
    [InlineData("success",  "w")]
    [InlineData("security", "w")]
    [InlineData("metric",   "w")]
    public void Extra_levels_w_spec_returns_lowercase_key(string levelKey, string spec)
        => SerilogLevelMoniker.Render(levelKey, spec).Should().Be(levelKey);

    [Theory]
    [InlineData("notice",   "u")]
    [InlineData("success",  "u")]
    [InlineData("security", "u")]
    [InlineData("metric",   "u")]
    public void Extra_levels_u_spec_returns_uppercase_key(string levelKey, string spec)
        => SerilogLevelMoniker.Render(levelKey, spec).Should().Be(levelKey.ToUpperInvariant());

    [Theory]
    [InlineData("notice")]
    [InlineData("success")]
    [InlineData("security")]
    [InlineData("metric")]
    public void Extra_levels_default_is_titlecase(string levelKey)
    {
        var expected = char.ToUpperInvariant(levelKey[0]) + levelKey.Substring(1);
        SerilogLevelMoniker.Render(levelKey, null).Should().Be(expected);
    }

    // ── Unknown specifier char — silent fallback to title-case ────────────────

    [Theory]
    [InlineData("information", "z")]
    [InlineData("warning",     "q")]
    public void Unknown_specifier_char_falls_back_to_titlecase(string levelKey, string spec)
        => SerilogLevelMoniker.Render(levelKey, spec).Should().Be(Titlecase(levelKey));

    // ── Oracle helper ─────────────────────────────────────────────────────────

    // Drive real Serilog's MessageTemplateTextFormatter with the given outputTemplate
    // for the given levelKey and extract only the {Level} token output.
    //
    // The formatter appends a trailing newline; we trim it before returning.
    // The spec is the full output template, e.g. "{Level:u3}" — the formatter
    // renders only the level token and returns that string.
    private static string RenderLevelToken(string levelKey, string outputTemplate)
    {
        var spec = new CanonicalEventSpec
        {
            LevelKey        = levelKey,
            MessageTemplate = "",
        };

        // SerilogFormattingOracle.RenderOutputTemplate drives real Serilog's
        // MessageTemplateTextFormatter end-to-end; the result has a trailing newline.
        var raw = SerilogFormattingOracle.RenderOutputTemplate(outputTemplate, spec);
        return raw.TrimEnd('\n', '\r');
    }

    // Inline title-case helper — mirrors the implementation to make test intent clear.
    private static string Titlecase(string key) => key switch
    {
        "verbose"     => "Verbose",
        "debug"       => "Debug",
        "information" => "Information",
        "warning"     => "Warning",
        "error"       => "Error",
        "fatal"       => "Fatal",
        "notice"      => "Notice",
        "success"     => "Success",
        "security"    => "Security",
        "metric"      => "Metric",
        _ => string.IsNullOrEmpty(key) ? key : char.ToUpperInvariant(key[0]) + key.Substring(1),
    };
}

