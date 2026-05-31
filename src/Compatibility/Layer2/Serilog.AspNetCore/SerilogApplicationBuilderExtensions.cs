#nullable enable
extern alias P6;

// Layer-2 mirror: SerilogApplicationBuilderExtensions — UseSerilogRequestLogging().
//
// Every member is a one-line forward to the P6 Layer-1 twin.
// P6 = MMP.Herald.Serilog.AspNetCore assembly (extern alias avoids the same-namespace
// collision with the Layer-1 class of the same name in Microsoft.AspNetCore.Builder).
//
// The configure callback bridge translates the Layer-2 RequestLoggingOptions wrapper
// back to the P6 inner instance. This adapter call is the minimum structural glue
// required when two assemblies declare distinct types for the same options class —
// the DRY-tripwire allowlist must acknowledge it (one wrapper projection per param adapter).

using System;
using Microsoft.AspNetCore.Builder;
using Serilog.AspNetCore;
using P6Ext = P6::Microsoft.AspNetCore.Builder.SerilogApplicationBuilderExtensions;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Extension methods that add Herald's request-logging middleware to the ASP.NET Core
/// pipeline, mirroring the real <c>Serilog.SerilogApplicationBuilderExtensions</c>.
/// Every member is a zero-logic forward to the P6 Layer-1 implementation.
/// </summary>
public static class SerilogApplicationBuilderExtensions
{
    // Parameterless overload — no options, no adapter needed.
    /// <summary>
    /// Adds Herald's request-logging middleware to the pipeline with default options.
    /// Forwards to P6's <c>UseSerilogRequestLogging</c>.
    /// </summary>
    public static IApplicationBuilder UseSerilogRequestLogging(
        this IApplicationBuilder app)
        => P6Ext.UseSerilogRequestLogging(app);

    // Overload with Action<RequestLoggingOptions> — bridges the Layer-2 options wrapper
    // to the P6 inner instance via RequestLoggingOptions.Inner.
    /// <summary>
    /// Adds Herald's request-logging middleware to the pipeline with custom options.
    /// Forwards to P6's <c>UseSerilogRequestLogging</c> via the Layer-2 options wrapper.
    /// </summary>
    public static IApplicationBuilder UseSerilogRequestLogging(
        this IApplicationBuilder app,
        Action<RequestLoggingOptions> configure)
        => P6Ext.UseSerilogRequestLogging(app, p6Opts =>
        {
            var wrapper = new RequestLoggingOptions(p6Opts);
            configure(wrapper);
        });
}
