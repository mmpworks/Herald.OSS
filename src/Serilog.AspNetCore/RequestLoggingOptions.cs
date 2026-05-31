#nullable enable

// RequestLoggingOptions — per-request HTTP summary configuration.
//
// Mirrors the shape of Serilog.AspNetCore's RequestLoggingOptions so
// consumers can swap the using statement and keep their configuration
// callbacks unchanged.
//
// IDiagnosticContext is a Serilog.AspNetCore-specific type that lives inside
// the real Serilog package and is not part of this Herald compat layer.
// The EnrichDiagnosticContext hook uses HttpContext directly instead —
// same information is available, no external type dependency, and the
// API is simpler for most enrichment scenarios.

using System;
using Microsoft.AspNetCore.Http;
using MMP.Herald.Serilog.Events;
using MEL = Microsoft.Extensions.Logging;

namespace MMP.Herald.Serilog.AspNetCore;

/// <summary>
/// Controls the behaviour of the per-request HTTP summary log line emitted
/// by <c>UseSerilogRequestLogging()</c>.
///
/// <para>
/// Drop-in replacement for <c>Serilog.AspNetCore.RequestLoggingOptions</c>.
/// The <c>EnrichDiagnosticContext</c> hook accepts <see cref="HttpContext"/>
/// directly rather than Serilog's <c>IDiagnosticContext</c>, which is an
/// internal type not available outside the real Serilog package.
/// </para>
/// </summary>
public sealed class RequestLoggingOptions
{
    /// <summary>
    /// Message template used for the per-request summary line.
    /// Must include <c>{RequestMethod}</c>, <c>{RequestPath}</c>,
    /// <c>{StatusCode}</c>, and <c>{Elapsed}</c> in that order.
    /// </summary>
    public string MessageTemplate { get; set; } =
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

    /// <summary>
    /// Determines the <see cref="LogEventLevel"/> for the summary line.
    /// Default: 5xx or exception → Error; 4xx → Warning; otherwise Information.
    ///
    /// <para>
    /// FM-8 mitigation: exceptions thrown from this callback are caught and
    /// suppressed — the summary line is always emitted, falling back to
    /// <see cref="LogEventLevel.Error"/>.
    /// </para>
    /// </summary>
    public Func<HttpContext, double, Exception?, LogEventLevel> GetLevel { get; set; } =
        static (ctx, _, ex) =>
            ex is not null || ctx.Response.StatusCode >= 500
                ? LogEventLevel.Error
                : ctx.Response.StatusCode >= 400
                    ? LogEventLevel.Warning
                    : LogEventLevel.Information;

    /// <summary>
    /// Optional callback to add extra properties to the request summary event.
    /// Called with the completed <see cref="HttpContext"/> after the inner
    /// middleware chain has run.
    ///
    /// <para>
    /// FM-8 mitigation: exceptions thrown from this callback are caught and
    /// suppressed — the summary line is always emitted even if enrichment fails.
    /// </para>
    /// </summary>
    public Action<HttpContext>? EnrichDiagnosticContext { get; set; }

    // ---------------------------------------------------------------------------
    // Internal helper: map LogEventLevel → MEL LogLevel for IsEnabled / Log calls.
    // LogEventLevel ordinal values (0–5) match MEL's Trace/Debug/Info/Warn/Error/Critical
    // exactly for the six overlapping levels, so a direct cast is safe.
    // ---------------------------------------------------------------------------

    internal static MEL.LogLevel ToMelLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose     => MEL.LogLevel.Trace,
        LogEventLevel.Debug       => MEL.LogLevel.Debug,
        LogEventLevel.Information => MEL.LogLevel.Information,
        LogEventLevel.Warning     => MEL.LogLevel.Warning,
        LogEventLevel.Error       => MEL.LogLevel.Error,
        LogEventLevel.Fatal       => MEL.LogLevel.Critical,
        _                         => MEL.LogLevel.Information,
    };
}
