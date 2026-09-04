#nullable enable

using System;
using FluentAssertions;
using MMP.Herald.Configuration;
using MMP.Herald.Configuration.Json;
using Xunit;

namespace MMP.Herald.OSS.Tests.ConfigSchemaVersion;

/// <summary>
/// The JSON config carries a schema version so a newer file cannot be read
/// silently by an older Herald. Without the version, a config written against
/// a future schema loads with every unknown field dropped, and the pipeline
/// starts in a shape nobody asked for.
/// </summary>
public sealed class ConfigSchemaVersionTests
{
    private static JsonLoggingConfig MinimalConfig(int schemaVersion) {
        return new JsonLoggingConfig(
            Levels: new JsonLogLevelsConfig([], [], []),
            MinimumLevel: "verbose",
            Async: new JsonAsyncLogPolicyConfig(Enabled: false),
            Batching: null,
            DumpRegisteredLevelsToConsole: false,
            Aliases: [],
            LevelStyles: [],
            Sinks: [],
            Routes: [],
            SchemaVersion: schemaVersion);
    }

    [Fact]
    public void Config_omitting_the_version_loads_as_version_one() {
        // A file written before the field existed must keep working.
        const string json = """
            {
              "levels": { "custom": [], "aliases": [], "removals": [] },
              "minimumLevel": "verbose",
              "async": { "enabled": false },
              "dumpRegisteredLevelsToConsole": false,
              "aliases": [],
              "levelStyles": [],
              "sinks": [],
              "routes": []
            }
            """;

        LoggingJsonSerializer.Deserialize(json).SchemaVersion.Should().Be(1);
    }

    [Fact]
    public void Supported_version_loads() {
        var json = LoggingJsonSerializer.Serialize(MinimalConfig(LoggingJsonSerializer.SupportedSchemaVersion));

        LoggingJsonSerializer.Deserialize(json).SchemaVersion
            .Should().Be(LoggingJsonSerializer.SupportedSchemaVersion);
    }

    [Fact]
    public void Unknown_version_is_refused_with_a_stable_code() {
        var json = LoggingJsonSerializer.Serialize(MinimalConfig(2));

        var act = () => LoggingJsonSerializer.Deserialize(json);

        act.Should().Throw<UnsupportedConfigSchemaVersionException>()
            .Which.Code.Should().Be(UnsupportedConfigSchemaVersionException.StableCode);
    }

    [Fact]
    public void Refusal_reports_both_versions() {
        var json = LoggingJsonSerializer.Serialize(MinimalConfig(7));

        var ex = Assert.Throws<UnsupportedConfigSchemaVersionException>(
            () => LoggingJsonSerializer.Deserialize(json));

        ex.SchemaVersion.Should().Be(7);
        ex.SupportedSchemaVersion.Should().Be(LoggingJsonSerializer.SupportedSchemaVersion);
    }

    /// <summary>
    /// Fuzz the version field. Only the supported version may load; every other
    /// value must be refused, including zero and negatives.
    /// </summary>
    [Fact]
    public void Fuzz_only_the_supported_version_loads() {
        const int seed = 20260904;
        var random = new Random(seed);

        for (var i = 0; i < 2_000; i++)
        {
            var version = random.Next(-50, 51);
            var json = LoggingJsonSerializer.Serialize(MinimalConfig(version));

            if (version == LoggingJsonSerializer.SupportedSchemaVersion)
            {
                LoggingJsonSerializer.Deserialize(json).SchemaVersion.Should().Be(
                    version, "version {0} is supported (seed {1})", version, seed);
            }
            else
            {
                Assert.Throws<UnsupportedConfigSchemaVersionException>(
                    () => LoggingJsonSerializer.Deserialize(json));
            }
        }
    }
}
