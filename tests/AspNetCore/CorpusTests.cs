#nullable enable

// G-CORPUS.3: ASP.NET Core wiring output-shape suite — Task 5 of P6.
//
// Verifies the structured output shape of UseSerilogRequestLogging()'s summary
// line by capturing LogEvent objects through the Herald pipeline.
//
// Test scope:
//   C1 — Exactly one summary line emitted per request.
//   C2 — Summary carries correct property names and runtime types:
//          RequestMethod (string) = "GET"
//          RequestPath   (string) = "/api/test"
//          StatusCode    (int)    = 200     ← FM-9: must be int, not string
//          Elapsed       (double) > 0.0     ← FM-4: from Stopwatch, always >= 0
//   C3 — 5xx responses map to Error level.
//   C4 — 4xx responses map to Warning level.
//
// Capture strategy:
//   TestLogPipelineBuilder + InMemoryLogSink captures LogEvent objects so tests
//   can inspect LogProperty.Value runtime types directly. The Herald pipeline is
//   wired via AddSerilog(heraldLogger) alongside ASP.NET Core's default providers.
//   Because multiple categories flow through (framework lines like "Request starting"),
//   tests filter events by MiddlewareCategory so only the middleware summary is seen.
//
//   For C3 and C4 (level assertions only), the CapturingLoggerProvider from
//   RequestLoggingMiddlewareTests is reused — it is lighter weight for the
//   cases where property types are not under test.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Testing;
using Xunit;

// Alias MEL LogLevel to avoid ambiguity with MMP.Herald.Levels.LogLevel
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Herald.OSS.Serilog.AspNetCore.Tests;

public sealed class CorpusTests
{
    // -------------------------------------------------------------------------
    // C1: exactly one summary line per request
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestLogging_emits_exactly_one_summary_line_per_request()
    {
        // Arrange
        var (heraldLogger, sink) = BuildCapturing();
        using var server = BuildServer(heraldLogger);
        using var client = server.CreateClient();

        // Act
        await client.GetAsync("/api/test");

        // Assert — filter to the middleware category so ASP.NET Core framework
        // lines ("Request starting", "Request finished") do not interfere.
        // Exactly one event for the middleware summary must arrive per request.
        MiddlewareEvents(sink).Should().HaveCount(1,
            "UseSerilogRequestLogging() must emit exactly one summary per request (G-CORPUS.3 C1)");
    }

    // -------------------------------------------------------------------------
    // C2: summary carries correct property names and runtime types
    //
    // FM-9 (StatusCode must be int, not string): the middleware passes
    // ctx.Response.StatusCode (int) as a positional arg to MEL.LoggerExtensions.Log().
    // MEL boxes the int into object? before storing it in the state KVP list.
    // HeraldMelLogger reads that KVP and stores it as a Boxed LogPropertyCompact,
    // whose Value property returns the original boxed int. Asserting IsType<int>
    // proves the value survived the round-trip as an integer, not a formatted string.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestLogging_summary_has_correct_property_names_and_types()
    {
        // Arrange
        var (heraldLogger, sink) = BuildCapturing();
        using var server = BuildServer(heraldLogger, endpointStatusCode: 200);
        using var client = server.CreateClient();

        // Act
        await client.GetAsync("/api/test");

        // Assert — filter to the middleware category (same isolation as C1).
        var events = MiddlewareEvents(sink);
        events.Should().HaveCount(1, "one request must produce one summary event");

        var evt = events[0];

        // --- RequestMethod ---
        var method = evt.GetProperty("RequestMethod");
        method.Should().NotBeNull("RequestMethod must be present");
        method!.Value.Value.Should().BeOfType<string>("RequestMethod must be a string");
        method.Value.Value.Should().Be("GET");

        // --- RequestPath ---
        var path = evt.GetProperty("RequestPath");
        path.Should().NotBeNull("RequestPath must be present");
        path!.Value.Value.Should().BeOfType<string>("RequestPath must be a string");
        ((string)path.Value.Value!).Should().EndWith("/api/test",
            "RequestPath must reflect the request URI");

        // --- StatusCode — FM-9: must be int, not string ---
        var status = evt.GetProperty("StatusCode");
        status.Should().NotBeNull("StatusCode must be present");
        status!.Value.Value.Should().BeOfType<int>(
            "FM-9: StatusCode must be carried as int; Serilog passes it typed and Herald must not coerce to string");
        status.Value.Value.Should().Be(200);

        // --- Elapsed — FM-4: must be double, must be non-negative ---
        var elapsed = evt.GetProperty("Elapsed");
        elapsed.Should().NotBeNull("Elapsed must be present");
        elapsed!.Value.Value.Should().BeOfType<double>(
            "FM-4: Elapsed must be double (from Stopwatch.Elapsed.TotalMilliseconds)");
        ((double)elapsed.Value.Value!).Should().BeGreaterThanOrEqualTo(0.0,
            "Elapsed from a monotonic Stopwatch is always >= 0");
    }

    // -------------------------------------------------------------------------
    // C3: 5xx responses use Error level
    //
    // Uses CapturingLoggerProvider (lightweight MEL capture) because only the
    // MEL log level is under test — no typed property inspection needed.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestLogging_5xx_uses_Error_level()
    {
        // Arrange — append CapturingLoggerProvider alongside any default providers;
        // filter to MiddlewareCategory so framework lines don't interfere.
        var cap = new CapturingLoggerProvider();
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddLogging(lb => lb.AddProvider(cap));
            })
            .Configure(app =>
            {
                app.UseSerilogRequestLogging();
                app.Run(ctx =>
                {
                    ctx.Response.StatusCode = 500;
                    return Task.CompletedTask;
                });
            });

        using var server = new TestServer(builder);
        using var client = server.CreateClient();

        // Act
        await client.GetAsync("/");

        // Assert
        var logs = cap.MiddlewareRecords;
        logs.Should().HaveCount(1, "one request → one summary (C3)");
        logs[0].Level.Should().Be(MelLogLevel.Error,
            "5xx status codes must map to Error level by the default GetLevel selector");
    }

    // -------------------------------------------------------------------------
    // C4: 4xx responses use Warning level
    //
    // Uses CapturingLoggerProvider for the same reason as C3.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestLogging_4xx_uses_Warning_level()
    {
        // Arrange
        var cap = new CapturingLoggerProvider();
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddLogging(lb => lb.AddProvider(cap));
            })
            .Configure(app =>
            {
                app.UseSerilogRequestLogging();
                app.Run(ctx =>
                {
                    ctx.Response.StatusCode = 404;
                    return Task.CompletedTask;
                });
            });

        using var server = new TestServer(builder);
        using var client = server.CreateClient();

        // Act
        await client.GetAsync("/missing");

        // Assert
        var logs = cap.MiddlewareRecords;
        logs.Should().HaveCount(1, "one request → one summary (C4)");
        logs[0].Level.Should().Be(MelLogLevel.Warning,
            "4xx status codes must map to Warning level by the default GetLevel selector");
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Build a capturing (StructuredLogger, InMemoryLogSink) pair at Verbose
    /// minimum level so no events are filtered out before reaching the sink.
    /// </summary>
    private static (MMP.Herald.Pipeline.StructuredLogger Logger, InMemoryLogSink Sink)
        BuildCapturing() =>
        new TestLogPipelineBuilder()
            .WithMinimumLevel(KnownLogLevels.Verbose)
            .Build();

    /// <summary>
    /// Build a TestServer that appends the Herald pipeline as an additional MEL
    /// provider. We do not call ClearProviders() so that the host starts cleanly
    /// (ASP.NET Core's internal providers are needed for the hosting pipeline).
    /// Tests isolate the middleware summary by filtering on MiddlewareCategory.
    /// </summary>
    private static TestServer BuildServer(
        MMP.Herald.Pipeline.StructuredLogger heraldLogger,
        int endpointStatusCode = 200)
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                // Append Herald as an additional provider — do not clear defaults.
                // Tests filter by MiddlewareCategory to isolate the summary line.
                services.AddLogging(lb => lb.AddSerilog(heraldLogger));
            })
            .Configure(app =>
            {
                app.UseSerilogRequestLogging();
                app.Run(ctx =>
                {
                    ctx.Response.StatusCode = endpointStatusCode;
                    return Task.CompletedTask;
                });
            });

        return new TestServer(builder);
    }

    /// <summary>
    /// Returns only the LogEvents emitted by the RequestLoggingMiddleware category,
    /// excluding ASP.NET Core framework lines that flow through the same pipeline.
    /// </summary>
    private static IReadOnlyList<LogEvent> MiddlewareEvents(InMemoryLogSink sink) =>
        sink.GetEvents()
            .Where(e => e.Category.Value == CapturingLoggerProvider.MiddlewareCategory)
            .ToList();
}
