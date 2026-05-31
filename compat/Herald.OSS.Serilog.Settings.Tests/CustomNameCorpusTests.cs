// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

// S-NEW-1 corpus — custom-named sink/enricher resolves via registry.
//
// The dangerous miss (pre-mortem Risk 10 follow-on, Rosanne seam):
//   A shop's in-house sink wired by name in appsettings.json had no resolution
//   path before the registry seam.  With S-NEW-1, one RegisterSink call suffices —
//   no parser fork needed.
//
// This suite verifies the end-to-end path:
//   1. Custom sink factory resolves and is invoked.
//   2. Args from config reach the factory intact.
//   3. Missing registration fails loud (SinkResolutionException), not silently.
//   4. Custom enricher factory follows the same S-NEW-1 pattern.

#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Herald.OSS.Serilog.Settings;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Herald.OSS.Serilog.Settings.Tests;

/// <summary>
/// S-NEW-1 end-to-end corpus for custom-named sink and enricher resolution.
///
/// Each test drives the full flow:
///   RegisterSink/RegisterEnricher → BuildMemoryConfig → ReadFrom.Configuration → assert.
///
/// The corpus also covers the failure mode: an unregistered custom name must throw
/// <see cref="SinkResolutionException"/> with the name populated, never silently no-op.
/// </summary>
public sealed class CustomNameCorpusTests
{
    // The S-NEW-1 scenario: a shop's in-house "AuditDatabaseSink" is wired by name in appsettings.json.
    // Without S-NEW-1, this is a fork-the-parser dead end. With it, one RegisterSink call suffices.
    [Fact]
    public void Custom_named_sink_from_config_resolves_via_registry()
    {
        var sinkWasCalled = false;
        var customReg = LoggerSinkRegistry.CreateDefault();
        customReg.RegisterSink("AuditDatabaseSink", (builder, args) =>
        {
            sinkWasCalled = true;
            builder.WriteTo.Null(); // stand-in for the real DB sink
            return builder;
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["Serilog:WriteTo:0:Name"] = "AuditDatabaseSink",
                ["Serilog:WriteTo:0:Args:connectionString"] = "Server=localhost;Database=Audit",
            })
            .Build();

        var act = () => new LoggerConfiguration()
            .ReadFrom.Configuration(config, sinkRegistry: customReg)
            .CreateLogger();
        act.Should().NotThrow("a registered custom name must resolve — no fork needed");
        sinkWasCalled.Should().BeTrue("the factory must have been invoked");
    }

    // Args from config reach the factory
    [Fact]
    public void Custom_sink_factory_receives_args_section()
    {
        string? capturedConnString = null;
        var customReg = LoggerSinkRegistry.CreateDefault();
        customReg.RegisterSink("AuditDatabaseSink", (builder, args) =>
        {
            capturedConnString = args["connectionString"];
            builder.WriteTo.Null();
            return builder;
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["Serilog:WriteTo:0:Name"] = "AuditDatabaseSink",
                ["Serilog:WriteTo:0:Args:connectionString"] = "Server=localhost;Database=Audit",
            })
            .Build();

        new LoggerConfiguration()
            .ReadFrom.Configuration(config, sinkRegistry: customReg)
            .CreateLogger();

        capturedConnString.Should().Be("Server=localhost;Database=Audit",
            "the Args section must be passed to the factory");
    }

    // Without registration, the same config fails loud (not silently)
    [Fact]
    public void Custom_sink_without_registration_fails_loud_not_silent()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["Serilog:WriteTo:0:Name"] = "AuditDatabaseSink",
            })
            .Build();

        var act = () => new LoggerConfiguration()
            .ReadFrom.Configuration(config)  // uses Default registry — no AuditDatabaseSink
            .CreateLogger();
        act.Should().Throw<SinkResolutionException>(
            "an unregistered custom name must fail loud, not silently no-op")
            .Which.SinkName.Should().Be("AuditDatabaseSink");
    }

    // Custom enricher via registry (same S-NEW-1 pattern)
    [Fact]
    public void Custom_named_enricher_from_config_resolves_via_registry()
    {
        var enricherWasCalled = false;
        var enrichReg = LoggerEnricherRegistry.CreateDefault();
        enrichReg.RegisterEnricher("TenantEnricher", (builder, args) =>
        {
            enricherWasCalled = true;
            return builder;
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["Serilog:Enrich:0:Name"] = "TenantEnricher",
                ["Serilog:WriteTo:0:Name"] = "Null",
            })
            .Build();

        new LoggerConfiguration()
            .ReadFrom.Configuration(config, enricherRegistry: enrichReg)
            .CreateLogger();

        enricherWasCalled.Should().BeTrue("the custom enricher factory must be invoked");
    }
}
