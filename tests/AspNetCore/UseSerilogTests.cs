#nullable enable

// Tests for UseSerilog() host-builder hook (P6 Task 3).
//
// Test scope:
//   H1 — UseSerilog(prebuilt StructuredLogger) wires the MEL provider.
//   H2 — UseSerilog(lambda overload) configures + wires via LoggerConfiguration.
//   H3 — FM-7: CloseAndFlush() called twice does not throw (idempotent).
//   H4 — UseSerilog(IHostApplicationBuilder, prebuilt StructuredLogger) wires the provider.
//   H5 — SerilogLifetimeService registers the ApplicationStopped flush hook.
//
// Lifecycle note: every test that calls UseSerilog(lambda) sets Log.Logger as a
// side-effect. Each test class resets it via xUnit's IDisposable convention so
// isolation is maintained across the suite.

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MMP.Herald.Levels;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Configuration;
using MMP.Herald.Testing;
using Xunit;

namespace Herald.OSS.Serilog.AspNetCore.Tests;

/// <summary>
/// End-to-end wiring tests for UseSerilog() on IHostBuilder.
/// </summary>
public sealed class UseSerilogTests : IDisposable
{
    // Reset the ambient logger after each test to avoid cross-test pollution.
    public void Dispose() => Log.Logger = SilentLogger.Instance;

    // -------------------------------------------------------------------------
    // H1: pre-built StructuredLogger overload
    // -------------------------------------------------------------------------

    [Fact]
    public void UseSerilog_prebuilt_logger_wires_mel_provider()
    {
        // Arrange — synchronous in-memory pipeline so assertions are deterministic.
        var (heraldLogger, sink) = new TestLogPipelineBuilder()
            .WithMinimumLevel(KnownLogLevels.Verbose)
            .Build();

        // Act — build a host wired through UseSerilog.
        using var host = Host.CreateDefaultBuilder()
            .UseSerilog(heraldLogger)
            .Build();

        var melLogger = host.Services.GetRequiredService<ILogger<UseSerilogTests>>();
        melLogger.LogWarning("disk {Pct}% full", 91);

        // Assert — event reaches the in-memory sink via the Herald pipeline.
        var events = sink.GetEvents();
        events.Should().HaveCount(1, "one LogWarning call must produce one captured event");
        events[0].Level.Key.Should().Be("warning");
    }

    // -------------------------------------------------------------------------
    // H2: lambda overload (LoggerConfiguration callback)
    // -------------------------------------------------------------------------

    [Fact]
    public void UseSerilog_lambda_overload_configures_and_wires_provider()
    {
        using var host = Host.CreateDefaultBuilder()
            .UseSerilog((ctx, sp, lc) =>
            {
                // Null sink satisfies Herald's minimum-sink requirement without
                // printing to the console during test runs. In production, callers
                // would use lc.WriteTo.Console() or lc.WriteTo.File(...) etc.
                _ = ctx; _ = sp;
                lc.WriteTo.Null();
            })
            .Build();

        // A resolved ILogger<T> should not throw — provider was registered.
        var melLogger = host.Services.GetRequiredService<ILogger<UseSerilogTests>>();
        var act = () => melLogger.LogInformation("lambda overload wired");
        act.Should().NotThrow("the lambda overload must produce a working MEL provider");
    }

    // -------------------------------------------------------------------------
    // H3: FM-7 — CloseAndFlush is idempotent (double-flush must not throw)
    // -------------------------------------------------------------------------

    [Fact]
    public void CloseAndFlush_called_twice_does_not_throw()
    {
        // Pre-mortem FM-7: the ApplicationStopped hook calls CloseAndFlush once;
        // if an app also calls it manually on shutdown, the second call must be
        // a safe no-op. P1's Interlocked.Exchange design guarantees this:
        // after the first call Logger is SilentLogger.Instance, which is not
        // IDisposable, so the second cast-and-dispose returns null.
        var act = () =>
        {
            Log.CloseAndFlush();
            Log.CloseAndFlush();
        };

        act.Should().NotThrow("CloseAndFlush is documented as idempotent (R-5 pin)");
    }

    // -------------------------------------------------------------------------
    // H4: IHostApplicationBuilder overload (modern generic host / WebApplication)
    // -------------------------------------------------------------------------

    [Fact]
    public void UseSerilog_host_application_builder_wires_mel_provider()
    {
        // Arrange
        var (heraldLogger, sink) = new TestLogPipelineBuilder()
            .WithMinimumLevel(KnownLogLevels.Verbose)
            .Build();

        // WebApplication.CreateBuilder() implements IHostApplicationBuilder.
        var appBuilder = WebApplication.CreateBuilder();
        appBuilder.UseSerilog(heraldLogger);

        using var app = appBuilder.Build();
        var melLogger = app.Services.GetRequiredService<ILogger<UseSerilogTests>>();
        melLogger.LogError("db {Code} fail", 503);

        // Assert
        var events = sink.GetEvents();
        events.Should().HaveCount(1, "LogError must produce exactly one event");
        events[0].Level.Key.Should().Be("error");
    }

    // -------------------------------------------------------------------------
    // H5: SerilogLifetimeService registers the ApplicationStopped hook
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SerilogLifetimeService_registers_flush_on_application_stopped()
    {
        // Arrange — wire a real SerilogLoggerAdapter so CloseAndFlush has
        // something to exchange. We verify the Logger is reset after the host stops.
        var (heraldLogger, _) = new TestLogPipelineBuilder().Build();
        var adapter = new SerilogLoggerAdapter(heraldLogger);
        Log.Logger = adapter;

        using var host = Host.CreateDefaultBuilder()
            .UseSerilog(heraldLogger)
            .Build();

        await host.StartAsync();

        // Stopping the host fires ApplicationStopped, which calls CloseAndFlush.
        await host.StopAsync();

        // After ApplicationStopped, Log.Logger must be SilentLogger.Instance
        // (CloseAndFlush swaps it atomically via Interlocked.Exchange).
        Log.Logger.Should().BeSameAs(SilentLogger.Instance,
            "CloseAndFlush() is registered on ApplicationStopped and resets Log.Logger");
    }
}
