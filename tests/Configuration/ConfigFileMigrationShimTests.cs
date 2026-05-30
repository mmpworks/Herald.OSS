#nullable enable

// S-3 migration shim tests — pins the on-disk config-file forward migration
// that lives in DefaultLoggingConfigurationMapper.NormalizeLevelKey.
// This is the SEPARATE boundary from the wire/registry boundary (Task 9 loud-reject).
//
// Contract:
//   - On-disk config files with old pre-rename keys (info/warn/critical/trace) load
//     cleanly through the mapper; the shim normalizes to Serilog-vocab on the way in.
//   - After a round-trip save the config carries the new key (the shim fires on load,
//     not on save — save always emits whatever key is in the runtime model).
//   - The wire/registry boundary (LogLevelRegistry.GetByKeyOrNull) still returns null
//     for old keys (the loud-reject contract is unaffected).
//   - New-vocab keys pass through the shim unchanged.

using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Configuration;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Levels;
using Xunit;

namespace MMP.Herald.OSS.Tests.ConfigFileMigration;

/// <summary>
/// Regression suite for the S-3 on-disk level-key forward migration shim
/// (<see cref="DefaultLoggingConfigurationMapper"/> — <c>NormalizeLevelKey</c>).
///
/// <para>
/// The shim is the config-FILE-LOAD boundary: it normalizes old pre-rename keys
/// (info/warn/critical/trace) to Serilog-vocab (information/warning/fatal/verbose)
/// before the registry lookup. It is separate from the wire/registry boundary,
/// which loud-rejects old keys (Task 9). These two boundaries must NOT share code.
/// </para>
///
/// <para>
/// The shim is deletable once all field deployments are migrated. These tests pin
/// its contract while it exists.
/// </para>
/// </summary>
public sealed class ConfigFileMigrationShimTests
{
    // Build a full registry (all ten levels) — same as the production bootstrap.
    private static ILogLevelRegistry BuildFullRegistry()
        => new DefaultLogLevelRegistryFactory().Create();

    // Build a minimal JsonLoggingConfig with a given minimumLevel key.
    // Sinks, routes, etc. are all empty — only minimumLevel is varied.
    private static JsonLoggingConfig BuildMinimalConfig(string minimumLevel) =>
        new(
            Levels: new JsonLogLevelsConfig(
                BaseLevels: [],
                AdditionalLevels: [],
                Placements: []),
            MinimumLevel: minimumLevel,
            Async: new JsonAsyncLogPolicyConfig(Enabled: false),
            Batching: null,
            DumpRegisteredLevelsToConsole: false,
            Aliases: [],
            LevelStyles: [],
            Sinks: [],
            Routes: []);

    // ── Main pipeline MinimumLevel shim ─────────────────────────────────────

    /// <summary>
    /// On-disk config files with old minimumLevel keys load with the Serilog-vocab
    /// key in the mapped runtime configuration. The shim fires at the config-load
    /// boundary — the registry never sees the old key.
    /// </summary>
    [Theory]
    [InlineData("info",     "information")]
    [InlineData("warn",     "warning")]
    [InlineData("critical", "fatal")]
    [InlineData("trace",    "verbose")]
    public void OnDiskConfig_with_old_minimumLevel_loads_with_new_key(string oldKey, string newKey)
    {
        var registry = BuildFullRegistry();
        var mapper = new DefaultLoggingConfigurationMapper();
        var config = BuildMinimalConfig(oldKey);

        // Act — should NOT throw KeyNotFoundException despite the old key
        var runtime = mapper.Map(config, registry);

        // The runtime pipeline's minimum level reflects the new Serilog-vocab key
        runtime.PipelinePolicy.MinimumLevel.Key.Should().Be(newKey,
            $"on-disk old key '{oldKey}' must be normalized to '{newKey}' by the S-3 shim");
    }

    /// <summary>
    /// New-vocab keys pass through the shim unchanged — they are already registered
    /// and resolve directly. The shim is a no-op for current keys.
    /// </summary>
    [Theory]
    [InlineData("verbose")]
    [InlineData("debug")]
    [InlineData("information")]
    [InlineData("warning")]
    [InlineData("error")]
    [InlineData("fatal")]
    public void OnDiskConfig_with_new_minimumLevel_passes_through_shim_unchanged(string currentKey)
    {
        var registry = BuildFullRegistry();
        var mapper = new DefaultLoggingConfigurationMapper();
        var config = BuildMinimalConfig(currentKey);

        var runtime = mapper.Map(config, registry);

        runtime.PipelinePolicy.MinimumLevel.Key.Should().Be(currentKey,
            $"current key '{currentKey}' must not be modified by the S-3 shim");
    }

    // ── Wire/registry boundary is UNAFFECTED by the shim ────────────────────

    /// <summary>
    /// The shim is in the mapper only. The wire/registry boundary (GetByKeyOrNull)
    /// still loud-rejects old keys — returning null — confirming the two boundaries
    /// are separate and the shim has not been re-added to the registry.
    /// </summary>
    [Theory]
    [InlineData("info")]
    [InlineData("warn")]
    [InlineData("critical")]
    [InlineData("trace")]
    public void Registry_loud_rejects_old_keys_regardless_of_shim(string oldKey)
    {
        var registry = BuildFullRegistry();

        // Direct registry call — no shim in this path
        var result = registry.GetByKeyOrNull(oldKey);

        result.Should().BeNull(
            $"GetByKeyOrNull must loud-reject old key '{oldKey}' (Task 9 contract); " +
            "the S-3 shim must not have been re-added to the registry");
    }

    // ── Sink MinLevel shim ───────────────────────────────────────────────────

    /// <summary>
    /// Sink-level MinLevel fields in on-disk configs also go through the shim.
    /// A sink with <c>minLevel: "info"</c> must resolve to the information level.
    /// </summary>
    [Theory]
    [InlineData("info",     "information")]
    [InlineData("warn",     "warning")]
    [InlineData("critical", "fatal")]
    [InlineData("trace",    "verbose")]
    public void OnDiskConfig_with_old_sink_minLevel_resolves_correctly(string oldKey, string newKey)
    {
        var registry = BuildFullRegistry();
        var mapper = new DefaultLoggingConfigurationMapper();

        // Build a config with a sink that has the old minLevel key
        var config = new JsonLoggingConfig(
            Levels: new JsonLogLevelsConfig([], [], []),
            MinimumLevel: "verbose",  // valid current key for the pipeline minimum
            Async: new JsonAsyncLogPolicyConfig(Enabled: false),
            Batching: null,
            DumpRegisteredLevelsToConsole: false,
            Aliases: [],
            LevelStyles: [],
            Sinks:
            [
                new JsonLogSinkConfig(
                    Name: "test-sink",
                    Kind: "null",
                    Enabled: true,
                    MinLevel: oldKey)
            ],
            Routes: []);

        var runtime = mapper.Map(config, registry);

        var sink = runtime.Sinks.Should().ContainSingle().Subject;
        sink.MinLevel.Should().NotBeNull($"sink MinLevel must resolve for old key '{oldKey}'");
        sink.MinLevel!.Key.Should().Be(newKey,
            $"sink old key '{oldKey}' must normalize to '{newKey}' via the S-3 shim");
    }
}
