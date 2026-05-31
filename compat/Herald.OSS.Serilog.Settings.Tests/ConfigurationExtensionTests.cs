// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

// G-CORPUS.2 — ReadFrom.Configuration round-trip test suite.
//
// Win condition: a Serilog-schema appsettings block applied via the extension
// method produces a live logger whose observable behaviour matches the config.
//
// Observable surface used for assertions (no access to internal Builder):
//   - ILogger.IsEnabled(level)  — proves MinimumLevel was applied.
//   - CreateLogger() does not throw — proves sink/enricher resolution succeeded.

#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Configuration;
using MMP.Herald.Serilog.Events;
using Herald.OSS.Serilog.Settings;
using Xunit;

namespace Herald.OSS.Serilog.Settings.Tests;

/// <summary>
/// G-CORPUS.2 — appsettings round-trip tests for
/// <see cref="ConfigurationLoggerConfigurationExtensions.Configuration"/>.
///
/// Each test drives the full flow:
///   BuildMemoryConfig → ReadFrom.Configuration → CreateLogger → assert.
///
/// Assertions use the public <see cref="ILogger.IsEnabled"/> surface so no
/// access to the internal <c>Builder</c> property is required.
/// </summary>
public sealed class ConfigurationExtensionTests
{
    // ── Shared helper ─────────────────────────────────────────────────────────

    /// <summary>
    /// Build an <see cref="IConfiguration"/> from a flat key/value dictionary.
    /// Keys use the standard colon-delimiter for nested sections
    /// (e.g. <c>"Serilog:MinimumLevel"</c>).
    /// </summary>
    private static IConfiguration BuildMemoryConfig(IDictionary<string, string?> pairs)
        => new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();

    // ── G-CORPUS.2 — MinimumLevel round-trip ─────────────────────────────────

    /// <summary>
    /// G-CORPUS.2-01: "Serilog:MinimumLevel": "Warning" sets the pipeline floor
    /// to Warning — Information and below are blocked, Warning and above are open.
    /// </summary>
    [Fact]
    public void ReadFrom_Configuration_applies_minimum_level_Warning()
    {
        var config = BuildMemoryConfig(new Dictionary<string, string?>
        {
            ["Serilog:MinimumLevel"] = "Warning",
            ["Serilog:WriteTo:0:Name"] = "Null",
        });

        var logger = new LoggerConfiguration()
            .ReadFrom.Configuration(config)
            .CreateLogger();

        // Warning and above must pass the floor.
        logger.IsEnabled(LogEventLevel.Warning).Should().BeTrue(
            "MinimumLevel=Warning must allow Warning events through");
        logger.IsEnabled(LogEventLevel.Error).Should().BeTrue(
            "MinimumLevel=Warning must allow Error events through");

        // Information is below the Warning floor.
        logger.IsEnabled(LogEventLevel.Information).Should().BeFalse(
            "MinimumLevel=Warning must block Information events");
        logger.IsEnabled(LogEventLevel.Debug).Should().BeFalse(
            "MinimumLevel=Warning must block Debug events");
    }

    /// <summary>
    /// G-CORPUS.2-02: "Serilog:MinimumLevel": "Debug" passes Debug and above.
    /// Verbose is the only blocked level.
    /// </summary>
    [Fact]
    public void ReadFrom_Configuration_applies_minimum_level_Debug()
    {
        var config = BuildMemoryConfig(new Dictionary<string, string?>
        {
            ["Serilog:MinimumLevel"] = "Debug",
            ["Serilog:WriteTo:0:Name"] = "Null",
        });

        var logger = new LoggerConfiguration()
            .ReadFrom.Configuration(config)
            .CreateLogger();

        logger.IsEnabled(LogEventLevel.Debug).Should().BeTrue();
        logger.IsEnabled(LogEventLevel.Information).Should().BeTrue();
        logger.IsEnabled(LogEventLevel.Verbose).Should().BeFalse(
            "MinimumLevel=Debug must block Verbose events");
    }

    /// <summary>
    /// G-CORPUS.2-03: Structured MinimumLevel object form with Default key.
    ///   "Serilog:MinimumLevel:Default": "Error"
    /// </summary>
    [Fact]
    public void ReadFrom_Configuration_applies_minimum_level_structured_Default()
    {
        var config = BuildMemoryConfig(new Dictionary<string, string?>
        {
            ["Serilog:MinimumLevel:Default"] = "Error",
            ["Serilog:WriteTo:0:Name"] = "Null",
        });

        var logger = new LoggerConfiguration()
            .ReadFrom.Configuration(config)
            .CreateLogger();

        logger.IsEnabled(LogEventLevel.Error).Should().BeTrue();
        logger.IsEnabled(LogEventLevel.Fatal).Should().BeTrue();
        logger.IsEnabled(LogEventLevel.Warning).Should().BeFalse(
            "MinimumLevel Default=Error must block Warning");
    }

    // ── G-CORPUS.2 — WriteTo sink round-trips ────────────────────────────────

    /// <summary>
    /// G-CORPUS.2-04: WriteTo:Console resolves without throwing.
    /// The Console sink is pre-seeded in <see cref="LoggerSinkRegistry.Default"/>.
    /// </summary>
    [Fact]
    public void ReadFrom_Configuration_applies_Console_sink()
    {
        var config = BuildMemoryConfig(new Dictionary<string, string?>
        {
            ["Serilog:WriteTo:0:Name"] = "Console",
        });

        var act = () => new LoggerConfiguration()
            .ReadFrom.Configuration(config)
            .CreateLogger();

        act.Should().NotThrow("Console is a pre-seeded sink and must resolve cleanly");
    }

    /// <summary>
    /// G-CORPUS.2-05: WriteTo:Null resolves without throwing.
    /// </summary>
    [Fact]
    public void ReadFrom_Configuration_applies_Null_sink()
    {
        var config = BuildMemoryConfig(new Dictionary<string, string?>
        {
            ["Serilog:WriteTo:0:Name"] = "Null",
        });

        var act = () => new LoggerConfiguration()
            .ReadFrom.Configuration(config)
            .CreateLogger();

        act.Should().NotThrow("Null sink is pre-seeded and must resolve cleanly");
    }

    /// <summary>
    /// G-CORPUS.2-06: Multiple sinks in one config are all applied — Console + Null.
    /// </summary>
    [Fact]
    public void ReadFrom_Configuration_applies_multiple_sinks()
    {
        var config = BuildMemoryConfig(new Dictionary<string, string?>
        {
            ["Serilog:WriteTo:0:Name"] = "Console",
            ["Serilog:WriteTo:1:Name"] = "Null",
        });

        var act = () => new LoggerConfiguration()
            .ReadFrom.Configuration(config)
            .CreateLogger();

        act.Should().NotThrow("both Console and Null are pre-seeded sinks");
    }

    // ── G-CORPUS.2 — Custom registry round-trip ───────────────────────────────

    /// <summary>
    /// G-CORPUS.2-07: A custom registry extended with a user-defined sink name
    /// resolves when passed explicitly to the extension method.
    /// </summary>
    [Fact]
    public void ReadFrom_Configuration_with_custom_registry_resolves_user_sink()
    {
        var customReg = LoggerSinkRegistry.CreateDefault();
        customReg.RegisterSink("MyCorp", (b, _) =>
        {
            b.WriteTo.Null();
            return b;
        });

        var config = BuildMemoryConfig(new Dictionary<string, string?>
        {
            ["Serilog:WriteTo:0:Name"] = "MyCorp",
        });

        var act = () => new LoggerConfiguration()
            .ReadFrom.Configuration(config, sinkRegistry: customReg)
            .CreateLogger();

        act.Should().NotThrow("custom-registered name must resolve via the supplied registry");
    }

    // ── G-CORPUS.2 — No-op on missing Serilog section ────────────────────────

    /// <summary>
    /// G-CORPUS.2-08: A config with no Serilog key at all is a no-op — no throw,
    /// logger builds with default settings.
    /// </summary>
    [Fact]
    public void ReadFrom_Configuration_missing_Serilog_section_is_noop()
    {
        var config = BuildMemoryConfig(new Dictionary<string, string?>
        {
            ["SomethingElse"] = "value",
        });

        // Must not throw; the logger will apply default minimum level.
        var act = () => new LoggerConfiguration()
            .ReadFrom.Configuration(config)
            .WriteTo.Null()         // ensure at least one sink so CreateLogger succeeds
            .CreateLogger();

        act.Should().NotThrow("a config with no Serilog section must be silently skipped");
    }

    // ── G-CORPUS.2 — Unknown sink name throws ─────────────────────────────────

    /// <summary>
    /// G-CORPUS.2-09: An unresolved WriteTo sink name throws
    /// <see cref="SinkResolutionException"/> — never silently drops the sink.
    /// This proves the Risk 2 contract from the pre-mortem.
    /// </summary>
    [Fact]
    public void ReadFrom_Configuration_unknown_sink_throws_SinkResolutionException()
    {
        var config = BuildMemoryConfig(new Dictionary<string, string?>
        {
            ["Serilog:WriteTo:0:Name"] = "Serilog.Sinks.Seq",
        });

        var act = () => new LoggerConfiguration()
            .ReadFrom.Configuration(config)
            .CreateLogger();

        act.Should().Throw<SinkResolutionException>(
            "an unresolved sink name must throw, not silently skip (Risk 2 contract)");
    }

    // ── G-CORPUS.2 — Enrich round-trip ───────────────────────────────────────

    /// <summary>
    /// G-CORPUS.2-10: Enrich:FromLogContext is a pre-seeded enricher and resolves
    /// without throwing.
    /// </summary>
    [Fact]
    public void ReadFrom_Configuration_applies_FromLogContext_enricher()
    {
        var config = BuildMemoryConfig(new Dictionary<string, string?>
        {
            ["Serilog:WriteTo:0:Name"] = "Null",
            ["Serilog:Enrich:0"]       = "FromLogContext",
        });

        var act = () => new LoggerConfiguration()
            .ReadFrom.Configuration(config)
            .CreateLogger();

        act.Should().NotThrow("FromLogContext is a pre-seeded enricher");
    }

    /// <summary>
    /// G-CORPUS.2-11: An unknown enricher name is silently skipped
    /// (enrichers are decorative; missing one must not prevent log delivery).
    /// This is the intentional asymmetry vs. sinks.
    /// </summary>
    [Fact]
    public void ReadFrom_Configuration_unknown_enricher_is_silently_skipped()
    {
        var config = BuildMemoryConfig(new Dictionary<string, string?>
        {
            ["Serilog:WriteTo:0:Name"] = "Null",
            ["Serilog:Enrich:0"]       = "Serilog.Enrichers.Environment",
        });

        var act = () => new LoggerConfiguration()
            .ReadFrom.Configuration(config)
            .CreateLogger();

        act.Should().NotThrow("unresolved enrichers must be silently skipped, not thrown");
    }

    // ── G-CORPUS.2 — Combined config round-trip ───────────────────────────────

    /// <summary>
    /// G-CORPUS.2-12: Full combined config — level + sink + enricher all applied
    /// in a single ReadFrom.Configuration call.
    /// </summary>
    [Fact]
    public void ReadFrom_Configuration_combined_config_applies_all_directives()
    {
        var config = BuildMemoryConfig(new Dictionary<string, string?>
        {
            ["Serilog:MinimumLevel"]  = "Warning",
            ["Serilog:WriteTo:0:Name"] = "Null",
            ["Serilog:Enrich:0"]      = "FromLogContext",
        });

        ILogger logger = null!;
        var act = () =>
        {
            logger = new LoggerConfiguration()
                .ReadFrom.Configuration(config)
                .CreateLogger();
        };

        act.Should().NotThrow();

        // Level was applied — Information is below Warning floor.
        logger.IsEnabled(LogEventLevel.Information).Should().BeFalse(
            "combined config: MinimumLevel=Warning must block Information");
        logger.IsEnabled(LogEventLevel.Warning).Should().BeTrue(
            "combined config: MinimumLevel=Warning must allow Warning through");
    }
}
