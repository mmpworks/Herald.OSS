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

    // ── G-LEVEL.1 — Old wire keys resolve to null after alias removal (Task 9) ────
    // Converted from scaffolding test (alias-map contract) to permanent contract test
    // (loud-reject contract). The alias map was removed in Task 9; these four keys
    // are no longer registered and must return null from GetByKeyOrNull.
    // On-disk config files use the S-3 migration shim in LoggingJsonSerializer — NOT
    // this registry path. This is the permanent regression pin for the loud-reject.

    /// <summary>
    /// After Task 9 alias removal, old pre-rename keys ("info", "warn",
    /// "critical", "trace") must NOT resolve through the registry. They are
    /// not registered; GetByKeyOrNull returns null (loud-reject). On-disk
    /// config files are handled by the S-3 migration shim before they reach
    /// the registry. This test is the permanent contract pin — without alias,
    /// old key resolves to null — intentional loud-reject.
    /// </summary>
    [Theory]
    [InlineData("info")]
    [InlineData("warn")]
    [InlineData("critical")]
    [InlineData("trace")]
    public void OldPersistedKey_resolves_to_null_without_alias(string oldKey)
    {
        var registry = BuildFullRegistry();

        var level = registry.GetByKeyOrNull(oldKey);

        // Without alias, old key resolves to null — intentional loud-reject.
        level.Should().BeNull(
            $"old key '{oldKey}' must return null after alias removal (Task 9); " +
            "on-disk config migration is handled by the S-3 shim, not the registry");
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

    // ── G-LEVEL.5 — Old wire keys are rejected loud after alias removal (Task 9) ─

    /// <summary>
    /// After the transitional alias map is removed in Task 9, old keys arriving
    /// from the wire or registry ("info", "warn", "critical", "trace") must
    /// return null from GetByKeyOrNull. This is the loud-reject contract: the
    /// wire boundary does NOT silently forward old keys; callers receive null
    /// and must surface the failure upstream. On-disk config files use the
    /// separate config-file migration shim (S-3) instead of this path.
    /// </summary>
    [Theory]
    [InlineData("info")]
    [InlineData("warn")]
    [InlineData("critical")]
    [InlineData("trace")]
    public void OldKeys_are_rejected_after_alias_removal(string oldKey)
    {
        var registry = new DefaultLogLevelRegistryFactory().Create();

        var result = registry.GetByKeyOrNull(oldKey);

        result.Should().BeNull(
            $"old key '{oldKey}' must be rejected after alias removal — Task 9 loud-reject contract");
    }
}
