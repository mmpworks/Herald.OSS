#nullable enable
extern alias P6;

// Layer-2 mirror: SerilogHostBuilderExtensions — UseSerilog() on IHostBuilder / IHostApplicationBuilder.
//
// Every member is a one-line forward to the P6 Layer-1 twin.
// P6 = MMP.Herald.Serilog.AspNetCore assembly (extern alias avoids the same-namespace collision:
// both the mirror and P6 place their SerilogHostBuilderExtensions in Microsoft.Extensions.Hosting).
//
// IWebHostBuilder.UseSerilog gap: real Serilog.AspNetCore exposes a
// SerilogWebHostBuilderExtensions class for IWebHostBuilder, but P6 does not cover it.
// That surface is FLAGGED as a cross-plan gap — see SerilogWebHostBuilderExtensions.cs.

using System;
using MMP.Herald.Pipeline;
using MMP.Herald.Serilog;
using Microsoft.Extensions.Hosting;
using P6Ext = P6::Microsoft.Extensions.Hosting.SerilogHostBuilderExtensions;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods that wire Herald into the .NET generic host, mirroring
/// the real <c>Serilog.SerilogHostBuilderExtensions</c> surface.
/// Every member is a zero-logic forward to the P6 Layer-1 implementation.
/// </summary>
public static class SerilogHostBuilderExtensions
{
    // -------------------------------------------------------------------------
    // IHostBuilder — pre-built StructuredLogger
    // -------------------------------------------------------------------------

    /// <summary>
    /// Clears existing MEL providers and registers <paramref name="logger"/>
    /// so all <c>ILogger&lt;T&gt;</c> instances route through Herald.
    /// Forwards to P6's <c>SerilogHostBuilderExtensions.UseSerilog</c>.
    /// </summary>
    public static IHostBuilder UseSerilog(
        this IHostBuilder builder,
        StructuredLogger logger)
        => P6Ext.UseSerilog(builder, logger);

    // -------------------------------------------------------------------------
    // IHostBuilder — lambda callback with LoggerConfiguration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Configures Herald via a <see cref="LoggerConfiguration"/> callback and
    /// registers the resulting logger with the DI container.
    /// Forwards to P6's <c>SerilogHostBuilderExtensions.UseSerilog</c>.
    /// </summary>
    public static IHostBuilder UseSerilog(
        this IHostBuilder builder,
        Action<HostBuilderContext, IServiceProvider, LoggerConfiguration> configure)
        => P6Ext.UseSerilog(builder, configure);

    // -------------------------------------------------------------------------
    // IHostApplicationBuilder — pre-built StructuredLogger (WebApplication / net9+)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Clears existing MEL providers and registers <paramref name="logger"/>
    /// for use with <c>WebApplication.CreateBuilder()</c>.
    /// Forwards to P6's <c>SerilogHostBuilderExtensions.UseSerilog</c>.
    /// </summary>
    public static IHostApplicationBuilder UseSerilog(
        this IHostApplicationBuilder builder,
        StructuredLogger logger)
        => P6Ext.UseSerilog(builder, logger);
}
