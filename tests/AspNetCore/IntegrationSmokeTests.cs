#nullable enable

// Cross-plan integration smoke (P1+P2+P5+P6 compose cleanly) — Task 6.
//
// Verifies that UseSerilog((ctx,svc,lc) => lc.ReadFrom.Configuration(ctx.Configuration))
// runs end-to-end through the TestHost without throwing:
//
//   P6 UseSerilog lambda
//     → P5 ReadFrom.Configuration (appsettings binding)
//       → P2 LoggerConfiguration translator → QuickLogBuilder → Herald logger
//         → P1 HeraldLoggerProvider registered in MEL
//
// These tests assert "no exception" — they are a compose-cleanly smoke, not an
// event-capture suite (that is G-CORPUS.3 Task 5).

using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MMP.Herald.Serilog.Configuration;
using Herald.OSS.Serilog.Settings;
using Xunit;

namespace Herald.OSS.Serilog.AspNetCore.Tests;

/// <summary>
/// Cross-plan integration smoke: P1+P2+P5+P6 compose cleanly end-to-end.
/// </summary>
public sealed class IntegrationSmokeTests
{
    // -------------------------------------------------------------------------
    // IS-1: full lambda path — UseSerilog calls ReadFrom.Configuration
    // -------------------------------------------------------------------------

    [Fact]
    public void UseSerilog_lambda_with_ReadFrom_Configuration_does_not_throw()
    {
        // Build a minimal IConfiguration with a Serilog section.
        var configValues = new Dictionary<string, string?>
        {
            ["Serilog:MinimumLevel"] = "Debug",
            ["Serilog:WriteTo:0:Name"] = "Console",
        };

        // The end-to-end smoke: P6 UseSerilog lambda calls P5 ReadFrom.Configuration
        // which calls P2 LoggerConfiguration → QuickLogBuilder → Herald logger
        // which is wrapped by P1 HeraldLoggerProvider and registered in MEL.
        var act = () => Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(configValues))
            .UseSerilog((ctx, svc, lc) =>
            {
                lc.ReadFrom.Configuration(ctx.Configuration);
            })
            .Build();

        act.Should().NotThrow("P1+P2+P5+P6 must compose cleanly end-to-end");
    }

    // -------------------------------------------------------------------------
    // IS-2: compile-check — P5 extension is discoverable from the P6 test project
    // -------------------------------------------------------------------------

    [Fact]
    public void ReadFrom_Configuration_extension_is_reachable_from_P6_test_project()
    {
        var lc = new LoggerConfiguration();
        var config = new ConfigurationBuilder().Build();

        // If this compiles and runs, P5's extension is on the P2 type and
        // discoverable from the P6 test project (ProjectReference is wired).
        var act = () => lc.ReadFrom.Configuration(config);
        act.Should().NotThrow("an empty Serilog config section is a no-op");
    }
}
