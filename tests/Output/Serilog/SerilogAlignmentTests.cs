#nullable enable

using FluentAssertions;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Serilog.Formatting;
using Xunit;

namespace MMP.Herald.OSS.Tests.Output.Serilog;

/// <summary>
/// Tests for <see cref="SerilogOutputTemplateRenderer.ApplyAlignment"/>.
///
/// <para>
/// Every edge case from the P3 Task 6 pre-mortem HIGH-2 finding is covered:
/// over-width (no truncation), zero (no-op), negative (left-align), positive
/// (right-align), and the combined specifier <c>{Level,-5:u3}</c>.
/// </para>
///
/// <para>
/// The oracle-parity test for the combined case (<c>Level_alignment_combined_with_format_matches_oracle</c>)
/// drives real Serilog 4.3.1 through <see cref="SerilogFormattingOracle"/> to confirm
/// that our alignment + level-moniker composition is byte-identical with Serilog's.
/// </para>
/// </summary>
public sealed class SerilogAlignmentTests
{
    // ── ApplyAlignment contract ───────────────────────────────────────────────

    [Theory]
    [InlineData("INF",         5,  "  INF")]           // positive → right-align (left-pad 2 spaces)
    [InlineData("INF",        -5,  "INF  ")]           // negative → left-align (right-pad 2 spaces)
    [InlineData("INF",         0,  "INF")]             // zero = no-op
    [InlineData("INFORMATION", 5,  "INFORMATION")]     // over-width: value wider than alignment → no truncation
    [InlineData("",            3,  "   ")]             // empty value → pure padding
    [InlineData("A",           1,  "A")]               // exact-width: no padding needed
    [InlineData("AB",         -1,  "AB")]              // over-width negative: no truncation
    [InlineData("X",          -3,  "X  ")]             // negative: pad right to width 3
    public void Alignment_pads_correctly(string value, int alignment, string expected)
        => SerilogOutputTemplateRenderer.ApplyAlignment(value, alignment)
           .Should().Be(expected, $"alignment={alignment} on '{value}' must produce '{expected}'");

    // ── Combined: alignment + format specifier via the full renderer ──────────

    /// <summary>
    /// <c>{Level,-5:u3}</c>: left-align in 5 chars, format u3 → "INF  " for Information.
    /// Confirms that ApplyAlignment is applied AFTER the level-moniker render and that
    /// the combination matches real Serilog 4.3.1's oracle output.
    /// </summary>
    [Fact]
    public void Level_alignment_combined_with_format_matches_oracle()
    {
        const string template = "{Level,-5:u3}";
        var spec = new CanonicalEventSpec
        {
            LevelKey        = "information",
            MessageTemplate = "",
        };

        // Oracle: drive real Serilog 4.3.1 and strip the trailing newline it appends.
        var expected = SerilogFormattingOracle.RenderOutputTemplate(template, spec)
                                             .TrimEnd('\n', '\r');

        // Herald composition: SerilogLevelMoniker.Render("information","u3") → "INF"
        //                     ApplyAlignment("INF", -5) → "INF  "
        var moniker = SerilogLevelMoniker.Render("information", "u3");   // "INF"
        var actual  = SerilogOutputTemplateRenderer.ApplyAlignment(moniker, -5);

        actual.Should().Be(expected,
            "left-align 5 of u3 moniker for 'information' must match real Serilog output");
        actual.Should().Be("INF  ", "u3 for information is INF; left-aligned to 5 chars adds 2 trailing spaces");
    }

    /// <summary>
    /// <c>{Level,5:u3}</c>: right-align in 5 chars → "  INF" for Information.
    /// Mirror of the left-align case to verify both padding directions are oracle-pinned.
    /// </summary>
    [Fact]
    public void Level_right_alignment_combined_with_format_matches_oracle()
    {
        const string template = "{Level,5:u3}";
        var spec = new CanonicalEventSpec
        {
            LevelKey        = "information",
            MessageTemplate = "",
        };

        var expected = SerilogFormattingOracle.RenderOutputTemplate(template, spec)
                                             .TrimEnd('\n', '\r');

        var moniker = SerilogLevelMoniker.Render("information", "u3");   // "INF"
        var actual  = SerilogOutputTemplateRenderer.ApplyAlignment(moniker, 5);

        actual.Should().Be(expected,
            "right-align 5 of u3 moniker for 'information' must match real Serilog output");
        actual.Should().Be("  INF", "u3 for information is INF; right-aligned to 5 chars adds 2 leading spaces");
    }

    // ── Alignment applied by Render() end-to-end ─────────────────────────────

    /// <summary>
    /// Verifies the <see cref="SerilogOutputTemplateRenderer.Render"/> entry point
    /// applies alignment when dispatching the Level token. This exercises the
    /// alignment path through the composing renderer, not just the helper directly.
    /// </summary>
    [Theory]
    [InlineData("information", "{Level,-5:u3}", "INF  ")]
    [InlineData("information", "{Level,5:u3}",  "  INF")]
    [InlineData("warning",     "{Level,-5:u3}", "WRN  ")]
    [InlineData("fatal",       "{Level,5:u3}",  "  FTL")]
    public void Render_applies_alignment_to_level_token(string levelKey, string template, string expected)
    {
        var evt = new FakeSerilogEventView
        {
            Level = MapLevel(levelKey),
        };

        var actual = SerilogOutputTemplateRenderer.Render(template, evt);
        actual.Should().Be(expected);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MMP.Herald.Serilog.Events.LogEventLevel MapLevel(string key) => key switch
    {
        "verbose"     => MMP.Herald.Serilog.Events.LogEventLevel.Verbose,
        "debug"       => MMP.Herald.Serilog.Events.LogEventLevel.Debug,
        "information" => MMP.Herald.Serilog.Events.LogEventLevel.Information,
        "warning"     => MMP.Herald.Serilog.Events.LogEventLevel.Warning,
        "error"       => MMP.Herald.Serilog.Events.LogEventLevel.Error,
        "fatal"       => MMP.Herald.Serilog.Events.LogEventLevel.Fatal,
        _             => MMP.Herald.Serilog.Events.LogEventLevel.Information,
    };
}

