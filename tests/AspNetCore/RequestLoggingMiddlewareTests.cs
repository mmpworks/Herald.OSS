#nullable enable

// RequestLoggingMiddleware integration tests — Task 4 of P6 ASP.NET Core wiring.
//
// Test scope:
//   R1 — One summary line per request (basic smoke).
//   R2 — StatusCode is read AFTER _next (FM-3): endpoint sets 404 after next runs.
//   R3 — Elapsed is non-negative (FM-4: Stopwatch, not DateTime).
//   R4 — Double registration emits only one line (FM-2: app.Properties sentinel).
//   R5 — GetLevel throwing still emits one summary line at Error (FM-8).
//   R6 — Exception mid-request still emits the summary line (FM-8 exception path).
//
// Capture strategy:
//   TestServer wires a CapturingLoggerProvider into MEL alongside the default
//   providers. The provider tracks each log record including category. Tests
//   filter to the RequestLoggingMiddleware category so ASP.NET Core framework
//   log lines ("Request starting", "Request finished") do not interfere.
//
// Note: IDiagnosticContext is a Serilog.AspNetCore-internal type not available in
// this compat layer. EnrichDiagnosticContext uses HttpContext directly instead.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MMP.Herald.Serilog.Events;
using Xunit;

// Alias MEL LogLevel to avoid ambiguity with MMP.Herald.Levels.LogLevel
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Herald.OSS.Serilog.AspNetCore.Tests;

// ---------------------------------------------------------------------------
// Captured log record — includes category so tests can filter by source.
// ---------------------------------------------------------------------------

internal sealed record CapturedLog(string Category, MelLogLevel Level, string Message);

// ---------------------------------------------------------------------------
// Capturing MEL provider — appended alongside ASP.NET Core's default providers.
// All records are kept; callers filter by Category to isolate the middleware.
// ---------------------------------------------------------------------------

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _records = new();

    // Full type name of the middleware logger — used by tests to filter records.
    public const string MiddlewareCategory =
        "MMP.Herald.Serilog.AspNetCore.RequestLoggingMiddleware";

    public ILogger CreateLogger(string categoryName)
        => new CapturingLogger(categoryName, _records);

    public IReadOnlyList<CapturedLog> Records => _records.ToArray();

    /// <summary>Records emitted by the RequestLoggingMiddleware category only.</summary>
    public IReadOnlyList<CapturedLog> MiddlewareRecords =>
        _records.Where(r => r.Category == MiddlewareCategory).ToArray();

    public void Dispose() { }

    private sealed class CapturingLogger : ILogger
    {
        private readonly string _category;
        private readonly ConcurrentQueue<CapturedLog> _queue;

        public CapturingLogger(string category, ConcurrentQueue<CapturedLog> queue)
        {
            _category = category;
            _queue = queue;
        }

        public bool IsEnabled(MelLogLevel logLevel) => logLevel != MelLogLevel.None;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public void Log<TState>(
            MelLogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == MelLogLevel.None) return;
            var message = formatter(state, exception);
            _queue.Enqueue(new CapturedLog(_category, logLevel, message));
        }
    }
}

// ---------------------------------------------------------------------------
// Test fixture
// ---------------------------------------------------------------------------

public sealed class RequestLoggingMiddlewareTests
{
    // -----------------------------------------------------------------------
    // Helper: build a TestServer.
    //
    // The capturing provider is appended — we keep ASP.NET Core's default
    // providers so the host starts cleanly, but isolate middleware records
    // by filtering on CapturingLoggerProvider.MiddlewareCategory.
    // -----------------------------------------------------------------------

    private static TestServer BuildDefaultServer(
        out CapturingLoggerProvider capture,
        int endpointStatusCode = 200)
    {
        var cap = new CapturingLoggerProvider();
        capture = cap;

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
                    ctx.Response.StatusCode = endpointStatusCode;
                    return Task.CompletedTask;
                });
            });

        return new TestServer(builder);
    }

    // -----------------------------------------------------------------------
    // R1: one summary line per request
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Middleware_emits_exactly_one_line_per_request()
    {
        using var server = BuildDefaultServer(out var capture);
        using var client = server.CreateClient();

        await client.GetAsync("/");

        capture.MiddlewareRecords.Should().HaveCount(1,
            "UseSerilogRequestLogging() must emit exactly one summary per request");
    }

    // -----------------------------------------------------------------------
    // R2: FM-3 — StatusCode is read after _next
    //
    // The endpoint sets the status code to 404 after _next returns.
    // If the middleware reads StatusCode before _next it gets 200 (default).
    // The summary must carry 404, proving the read happens post-_next.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Status_code_is_read_after_next_middleware()
    {
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

                // Endpoint explicitly sets 404 — the status code the summary must carry.
                app.Run(ctx =>
                {
                    ctx.Response.StatusCode = 404;
                    return Task.CompletedTask;
                });
            });

        using var server = new TestServer(builder);
        using var client = server.CreateClient();

        await client.GetAsync("/orders");

        var logs = cap.MiddlewareRecords;
        logs.Should().HaveCount(1, "one request → one summary");

        // The summary message must mention 404, not 200.
        logs[0].Message.Should().Contain("404",
            "StatusCode must be read AFTER _next runs (FM-3); endpoint set 404");

        // Level must be Warning (4xx rule).
        logs[0].Level.Should().Be(MelLogLevel.Warning,
            "4xx responses map to Warning by default");
    }

    // -----------------------------------------------------------------------
    // R3: FM-4 — Elapsed is non-negative (Stopwatch, not DateTime)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Elapsed_is_non_negative_monotonic()
    {
        double capturedElapsed = double.MinValue;
        var cap = new CapturingLoggerProvider();

        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddLogging(lb => lb.AddProvider(cap));
            })
            .Configure(app =>
            {
                app.UseSerilogRequestLogging(opts =>
                {
                    // Intercept GetLevel to capture the elapsed value before formatting.
                    var originalGetLevel = opts.GetLevel;
                    opts.GetLevel = (ctx, elapsed, ex) =>
                    {
                        capturedElapsed = elapsed;
                        return originalGetLevel(ctx, elapsed, ex);
                    };
                });

                app.Run(ctx =>
                {
                    ctx.Response.StatusCode = 200;
                    return Task.CompletedTask;
                });
            });

        using var server = new TestServer(builder);
        using var client = server.CreateClient();

        await client.GetAsync("/");

        capturedElapsed.Should().BeGreaterThanOrEqualTo(0,
            "Elapsed comes from Stopwatch which is monotonic and always >= 0 (FM-4)");
    }

    // -----------------------------------------------------------------------
    // R4: FM-2 — double registration emits only one summary line
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Double_registration_emits_only_one_line()
    {
        var cap = new CapturingLoggerProvider();

        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddLogging(lb => lb.AddProvider(cap));
            })
            .Configure(app =>
            {
                // Register twice — the sentinel must make the second call a no-op.
                app.UseSerilogRequestLogging();
                app.UseSerilogRequestLogging();

                app.Run(ctx =>
                {
                    ctx.Response.StatusCode = 200;
                    return Task.CompletedTask;
                });
            });

        using var server = new TestServer(builder);
        using var client = server.CreateClient();

        await client.GetAsync("/");

        cap.MiddlewareRecords.Should().HaveCount(1,
            "FM-2: second UseSerilogRequestLogging() call must be a no-op via app.Properties sentinel");
    }

    // -----------------------------------------------------------------------
    // R5: FM-8 — GetLevel throwing still emits exactly one summary at Error
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Exception_in_GetLevel_still_emits_one_summary_line_at_Error()
    {
        var cap = new CapturingLoggerProvider();

        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddLogging(lb => lb.AddProvider(cap));
            })
            .Configure(app =>
            {
                app.UseSerilogRequestLogging(opts =>
                {
                    // GetLevel always throws — summary must still appear.
                    opts.GetLevel = (_, _, _) =>
                        throw new InvalidOperationException("GetLevel boom");
                });

                app.Run(ctx =>
                {
                    ctx.Response.StatusCode = 200;
                    return Task.CompletedTask;
                });
            });

        using var server = new TestServer(builder);
        using var client = server.CreateClient();

        await client.GetAsync("/");

        var logs = cap.MiddlewareRecords;
        logs.Should().HaveCount(1,
            "FM-8: throwing GetLevel must not suppress the summary line");

        logs[0].Level.Should().Be(MelLogLevel.Error,
            "FM-8: when GetLevel throws the fallback level is Error");
    }

    // -----------------------------------------------------------------------
    // R6: FM-8 — exception mid-request still emits the summary line
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Exception_mid_request_still_emits_summary_line()
    {
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

                // Endpoint throws — the middleware finally block must still emit.
                app.Run(_ => throw new InvalidOperationException("endpoint boom"));
            });

        using var server = new TestServer(builder);
        using var client = server.CreateClient();

        // The server will surface the exception. We don't care about the HTTP
        // response status — only that the finally block fired and emitted the summary.
        try
        {
            await client.GetAsync("/");
        }
        catch
        {
            // TestServer may surface the exception as HttpRequestException.
            // Either way the middleware finally block must have fired.
        }

        // The summary must have been emitted at Error (exception → GetLevel default → Error).
        cap.MiddlewareRecords.Should().Contain(l => l.Level == MelLogLevel.Error,
            "FM-8: a throwing endpoint must still produce an Error summary via the finally block");
    }
}
