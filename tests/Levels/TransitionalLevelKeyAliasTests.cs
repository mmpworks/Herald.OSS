#nullable enable

using Xunit;
using MMP.Herald.Levels;

namespace MMP.Herald.OSS.Tests.Levels;

/// <summary>
/// Unit guard for the transitional old->new level-key alias map used during
/// the Serilog rename wave. This scaffolding is removed in Task 9; the tests
/// pin its mapping contract while it exists so the rename sweep can rely on it.
/// </summary>
public sealed class TransitionalLevelKeyAliasTests
{
    [Theory]
    [InlineData("info", "information")]
    [InlineData("warn", "warning")]
    [InlineData("critical", "fatal")]   // value rename to a previously-nonexistent key — the trap
    [InlineData("trace", "verbose")]
    [InlineData("information", "information")] // new keys pass through unchanged
    [InlineData("debug", "debug")]
    [InlineData("notice", "notice")]    // extras untouched
    public void Canonicalize_maps_old_keys_to_new(string input, string expected)
        => Assert.Equal(expected, TransitionalLevelKeyAliases.Canonicalize(input));

    [Fact]
    public void Canonicalize_is_case_insensitive()
        => Assert.Equal("information", TransitionalLevelKeyAliases.Canonicalize("INFO"));
}
