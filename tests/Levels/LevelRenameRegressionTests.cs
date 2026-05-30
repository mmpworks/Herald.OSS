#nullable enable

// G-LEVEL regression suite — pins every lockstep hazard the pre-mortem identified.
// These tests are the guards that let Task 9 safely remove the alias map.
// All should PASS now (the rename is complete). A failing test is a real gap.

using FluentAssertions;
using MMP.Herald.Levels;
using Xunit;

namespace MMP.Herald.OSS.Tests.Levels;

/// <summary>
/// G-LEVEL regression suite for the Serilog rename wave.
/// Pins the alias map contract (G-LEVEL.1), extra-level survival (G-LEVEL.4),
/// and direct new-vocab resolution (G-LEVEL.5 preview).
///
/// <para>
/// Registry construction pattern: <see cref="DefaultLogLevelRegistryFactory.Create"/>
/// — same pattern used in production bootstrap, includes all ten levels
/// (verbose/debug/information/warning/error/fatal/notice/success/security/metric).
/// </para>
/// </summary>
public sealed class LevelRenameRegressionTests
{
    // Build a registry with the full production level set — all ten levels
    // (the Serilog six + the four Herald extras). Uses DefaultLogLevelRegistryFactory
    // so the test exercises the same code path as the real application bootstrap.
    private static ILogLevelRegistry BuildFullRegistry()
        => new DefaultLogLevelRegistryFactory().Create();

    // ── G-LEVEL.1 — Old persisted JSON keys resolve through the alias map ──────

    /// <summary>
    /// Pre-rename keys that survive in persisted configs ("info", "warn",
    /// "critical", "trace") MUST resolve through the transitional alias map
    /// to their Serilog-vocab successors. This is the primary guard that
    /// prevents a config-upgrade regression on Task 9 removal.
    /// </summary>
    [Theory]
    [InlineData("info",     "information")]
    [InlineData("warn",     "warning")]
    [InlineData("critical", "fatal")]      // value rename to previously-nonexistent key — the trap
    [InlineData("trace",    "verbose")]
    public void OldPersistedKey_resolves_to_new_level(string oldKey, string newKey)
    {
        var registry = BuildFullRegistry();

        var level = registry.GetByKeyOrNull(oldKey);

        level.Should().NotBeNull($"old key '{oldKey}' should resolve through the alias map");
        level!.Key.Should().Be(newKey,
            $"old key '{oldKey}' must canonicalize to Serilog-vocab key '{newKey}', not pass through as-is");
    }

    // ── G-LEVEL.4 — The four extra levels survive and remain accessible ─────────

    /// <summary>
    /// Herald's four extra levels (notice/success/security/metric) must survive
    /// the rename wave unchanged — they are not in the Serilog vocabulary and
    /// must not be aliased to any other key.
    /// </summary>
    [Theory]
    [InlineData("notice")]
    [InlineData("success")]
    [InlineData("security")]
    [InlineData("metric")]
    public void ExtraLevels_survive_rename_and_resolve(string key)
    {
        var registry = BuildFullRegistry();

        var level = registry.GetByKeyOrNull(key);

        level.Should().NotBeNull($"extra level '{key}' must survive the rename and be resolvable");
        level!.Key.Should().Be(key,
            $"extra level '{key}' must keep its own key — it must not be aliased to another key");
    }

    // ── G-LEVEL.5 preview — New keys resolve directly without alias ─────────────

    /// <summary>
    /// New-vocab keys ("information", "warning", "fatal", "verbose") must
    /// resolve directly from the registry without needing the alias map.
    /// This preview pin verifies the rename is fully wired; the full G-LEVEL.5
    /// test (alias-map removed) is added in Task 9.
    /// </summary>
    [Theory]
    [InlineData("information")]
    [InlineData("warning")]
    [InlineData("fatal")]
    [InlineData("verbose")]
    public void NewVocabKey_resolves_directly(string key)
    {
        var registry = BuildFullRegistry();

        var level = registry.GetByKeyOrNull(key);

        level.Should().NotBeNull($"new key '{key}' must resolve directly from the registry");
        level!.Key.Should().Be(key,
            $"new key '{key}' must return itself as its canonical key");
    }
}
