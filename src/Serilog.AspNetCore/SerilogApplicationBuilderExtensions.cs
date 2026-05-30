#nullable enable

// SerilogApplicationBuilderExtensions — UseSerilogRequestLogging() for IApplicationBuilder.
//
// FM-2 mitigation: app.Properties["Herald.SerilogRequestLogging"] sentinel prevents
// double registration. Calling UseSerilogRequestLogging() twice on the same
// IApplicationBuilder is a safe no-op after the first call.
//
// Namespace is Microsoft.AspNetCore.Builder so callers get the extension method
// without an extra using statement — the same ergonomics as Serilog's own package.

using System;
using MMP.Herald.Serilog.AspNetCore;
using Microsoft.AspNetCore.Builder;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Extension methods that add Herald's request-logging middleware to the
/// ASP.NET Core pipeline as a drop-in replacement for Serilog's
/// <c>app.UseSerilogRequestLogging()</c>.
/// </summary>
public static class SerilogApplicationBuilderExtensions
{
    // Key used in app.Properties to detect and prevent double registration.
    // Internal so tests can verify the sentinel directly.
    internal const string SentinelKey = "Herald.SerilogRequestLogging";

    /// <summary>
    /// Adds the Herald request-logging middleware to the pipeline.
    /// Emits one structured log line per HTTP request with
    /// <c>RequestMethod</c>, <c>RequestPath</c>, <c>StatusCode</c>, and
    /// <c>Elapsed</c> properties.
    ///
    /// <para>
    /// FM-2 mitigation: a second call on the same <see cref="IApplicationBuilder"/>
    /// is a silent no-op. Only one summary line is emitted per request.
    /// </para>
    /// </summary>
    /// <param name="app">The application builder to add the middleware to.</param>
    /// <param name="configure">
    ///   Optional callback to customise <see cref="RequestLoggingOptions"/>
    ///   (message template, level selector, enrichment hook).
    /// </param>
    /// <returns>The <paramref name="app"/> for chaining.</returns>
    public static IApplicationBuilder UseSerilogRequestLogging(
        this IApplicationBuilder app,
        Action<RequestLoggingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        // FM-2: sentinel check — do not register the middleware twice.
        if (app.Properties.ContainsKey(SentinelKey))
            return app;

        app.Properties[SentinelKey] = true;

        var opts = new RequestLoggingOptions();
        configure?.Invoke(opts);

        // UseMiddleware<T> resolves ILogger<RequestLoggingMiddleware> from DI
        // and passes opts as an extra constructor argument.
        return app.UseMiddleware<RequestLoggingMiddleware>(opts);
    }
}
