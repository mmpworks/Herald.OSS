#nullable enable

// RequestLoggingMiddleware — per-request HTTP summary log line.
//
// Correctness pins (from P6 pre-mortem):
//
//   FM-3: StatusCode is read AFTER await _next(ctx), never before.
//         The inner middleware chain sets it; reading it before _next
//         always returns 200 (the ASP.NET Core default).
//
//   FM-4: Elapsed is measured with Stopwatch (monotonic clock), not
//         DateTime subtraction. DateTime can go backwards across DST
//         boundaries and NTP corrections; Stopwatch cannot.
//
//   FM-8: User-supplied callbacks (GetLevel, EnrichDiagnosticContext) are
//         each wrapped in try/catch. A throwing callback must not suppress
//         the summary line. GetLevel failures default to Error so the
//         anomaly is visible; EnrichDiagnosticContext failures are silently
//         swallowed (enrichment is best-effort).
//
// The summary is emitted in the finally block so it fires even when the
// inner pipeline throws (FM-3 / exception path).

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using MMP.Herald.Serilog.AspNetCore;
using MMP.Herald.Serilog.Events;
using MEL = Microsoft.Extensions.Logging;

namespace MMP.Herald.Serilog.AspNetCore;

/// <summary>
/// ASP.NET Core middleware that emits one structured log line per HTTP request,
/// matching the output shape of <c>Serilog.AspNetCore</c>'s request-logging
/// middleware.
///
/// <para>
/// Register via <see cref="SerilogApplicationBuilderExtensions.UseSerilogRequestLogging"/>.
/// </para>
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly MEL.ILogger _logger;
    private readonly RequestLoggingOptions _options;

    // Primary constructor kept compatible with ASP.NET Core's UseMiddleware<T>
    // convention: DI injects _next and _logger; options arrive as an explicit
    // parameter passed by the extension method.
    //
    // ILogger<RequestLoggingMiddleware> is accepted as ILogger<T> so DI can
    // inject it; we store it as ILogger (the base interface) so MEL extension
    // methods (IsEnabled, Log) resolve without ambiguity against Herald's own
    // LogLevel record type.
    public RequestLoggingMiddleware(
        RequestDelegate next,
        MEL.ILogger<RequestLoggingMiddleware> logger,
        RequestLoggingOptions options)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Executes the inner pipeline and emits one summary log line when it completes
    /// or throws.
    /// </summary>
    public async Task InvokeAsync(HttpContext ctx)
    {
        // FM-4: monotonic clock; never DateTime.
        var sw = Stopwatch.StartNew();
        Exception? caughtEx = null;

        try
        {
            // FM-3: status code is set by the inner pipeline — must NOT be read
            // until after this await returns (or throws).
            await _next(ctx);
        }
        catch (Exception ex)
        {
            // Capture for GetLevel; re-throw so the host error handler still fires.
            caughtEx = ex;
            throw;
        }
        finally
        {
            sw.Stop();
            EmitSummary(ctx, sw.Elapsed.TotalMilliseconds, caughtEx);
        }
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    private void EmitSummary(HttpContext ctx, double elapsedMs, Exception? caughtEx)
    {
        // FM-8: user GetLevel may throw — fall back to Error so the anomaly is
        // surfaced rather than silently promoted to Information.
        LogEventLevel eventLevel;
        try
        {
            eventLevel = _options.GetLevel(ctx, elapsedMs, caughtEx);
        }
        catch
        {
            eventLevel = LogEventLevel.Error;
        }

        var melLevel = RequestLoggingOptions.ToMelLevel(eventLevel);

        // Skip allocation and formatting if the level is filtered out.
        if (!_logger.IsEnabled(melLevel))
            return;

        // FM-8: user EnrichDiagnosticContext may throw — swallow; the summary
        // line must always appear regardless of enricher failure.
        try
        {
            _options.EnrichDiagnosticContext?.Invoke(ctx);
        }
        catch
        {
            // Intentionally swallowed — enrichment is best-effort.
        }

        // Emit the summary. Properties are positional, matching the default
        // message template:
        //   "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms"
        //
        // FM-3: ctx.Response.StatusCode is read HERE, after _next completed.
        MEL.LoggerExtensions.Log(
            _logger,
            melLevel,
            _options.MessageTemplate,
            ctx.Request.Method,
            ctx.Request.Path.Value,
            ctx.Response.StatusCode,
            elapsedMs);
    }
}
