#nullable enable

using FluentAssertions;
using Herald.OSS.Serilog.Expressions.Filtering;
using Xunit;

namespace Herald.OSS.Serilog.Expressions.Tests;

/// <summary>
/// F1 — the most dangerous narrative. <c>@Level</c> comparisons MUST use
/// severity rank (Serilog enum ordinal), not a lexical string compare. A string
/// compare would order "Error" &lt; "Warning" and drop the wrong events.
///
/// <para>
/// The corpus pins every meaningful level-pair ordering. <c>@Level &gt;=
/// 'Warning'</c> must accept Warning/Error/Fatal and reject Verbose/Debug/
/// Information. Equality on the level still works by name.
/// </para>
/// </summary>
public sealed class LevelRankCorpusTests
{
    // ByIncludingOnly admits an event when the expression is true. So a true
    // expectation means the filter admits the event of that level.
    private static bool Admits(string expression, string levelKey)
    {
        var filter = Filter.ByIncludingOnly(expression);
        return filter.Allow(EventBuilder.Build(level: EventBuilder.Level(levelKey)));
    }

    [Theory]
    // @Level >= 'Warning' — rank fence at Warning.
    [InlineData("@Level >= 'Warning'", "verbose", false)]
    [InlineData("@Level >= 'Warning'", "debug", false)]
    [InlineData("@Level >= 'Warning'", "information", false)]
    [InlineData("@Level >= 'Warning'", "warning", true)]
    [InlineData("@Level >= 'Warning'", "error", true)]
    [InlineData("@Level >= 'Warning'", "fatal", true)]
    // @Level > 'Information' — strict.
    [InlineData("@Level > 'Information'", "information", false)]
    [InlineData("@Level > 'Information'", "warning", true)]
    // @Level <= 'Information' — low end.
    [InlineData("@Level <= 'Information'", "verbose", true)]
    [InlineData("@Level <= 'Information'", "debug", true)]
    [InlineData("@Level <= 'Information'", "information", true)]
    [InlineData("@Level <= 'Information'", "warning", false)]
    [InlineData("@Level <= 'Information'", "error", false)]
    // @Level < 'Error'
    [InlineData("@Level < 'Error'", "warning", true)]
    [InlineData("@Level < 'Error'", "error", false)]
    [InlineData("@Level < 'Error'", "fatal", false)]
    // @Level >= 'Verbose' — accepts everything mapped.
    [InlineData("@Level >= 'Verbose'", "verbose", true)]
    [InlineData("@Level >= 'Verbose'", "fatal", true)]
    // @Level >= 'Fatal' — only Fatal.
    [InlineData("@Level >= 'Fatal'", "error", false)]
    [InlineData("@Level >= 'Fatal'", "fatal", true)]
    public void LevelRank_orders_by_severity_not_string(string expression, string levelKey, bool expected) =>
        Admits(expression, levelKey).Should().Be(expected);

    [Theory]
    // Equality by name still works — @Level = 'Error' is exact.
    [InlineData("@Level = 'Error'", "error", true)]
    [InlineData("@Level = 'Error'", "warning", false)]
    [InlineData("@Level = 'Warning'", "warning", true)]
    [InlineData("@Level = 'Information'", "information", true)]
    public void LevelEquality_matches_by_name(string expression, string levelKey, bool expected) =>
        Admits(expression, levelKey).Should().Be(expected);

    [Theory]
    // @Level equality on a HERALD-ONLY level (no Serilog ordinal) must match by
    // name identity, not rank. A rank compare returns null here and the event
    // would silently never match — the bug this corpus pins.
    [InlineData("@Level = 'Notice'", "notice", true)]      // display-name literal vs key
    [InlineData("@Level = 'notice'", "notice", true)]      // lowercase literal — identity is case-insensitive
    [InlineData("@Level = 'Notice'", "error", false)]      // different level rejects
    [InlineData("@Level = 'Success'", "success", true)]
    [InlineData("@Level = 'Security'", "security", true)]
    [InlineData("@Level = 'Metric'", "metric", true)]
    // @Level != on a Herald-only level — Notice is NOT Error, so != is true (admits).
    [InlineData("@Level != 'Error'", "notice", true)]
    [InlineData("@Level != 'Notice'", "notice", false)]
    public void LevelEquality_matches_Herald_only_levels_by_identity(string expression, string levelKey, bool expected) =>
        Admits(expression, levelKey).Should().Be(expected);

    [Fact]
    public void LevelRank_on_Herald_only_level_is_undefined_and_does_not_admit()
    {
        // A Herald-only level has no Serilog ordinal, so a RANK op against it is
        // genuinely indeterminate → undefined. ByIncludingOnly admits only on a
        // definite true, so undefined rejects. This is the agreed behavior: only
        // equality (=/!=) works for Herald-only levels; >=/</>/<= do not.
        Admits("@Level >= 'Notice'", "notice").Should().BeFalse(
            "a Herald-only level has no defined rank relationship to the Serilog scale");
    }

    [Fact]
    public void Proof_string_compare_would_be_wrong()
    {
        // Lexically "Error" < "Warning" (E < W). If the implementation did a
        // string compare, @Level >= 'Warning' would REJECT Error. The rank
        // binding makes it ACCEPT Error. This is the exact F1 trap.
        Admits("@Level >= 'Warning'", "error").Should().BeTrue(
            "Error outranks Warning by severity even though it sorts earlier alphabetically");
    }
}
