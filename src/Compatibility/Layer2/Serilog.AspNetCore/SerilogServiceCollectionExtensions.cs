#nullable enable
extern alias P6;

// Layer-2 mirror: SerilogServiceCollectionExtensions / SerilogLoggingBuilderExtensions.
//
// Real Serilog exposes AddSerilog() in two places:
//   1. ILoggingBuilder.AddSerilog(...)     — in Microsoft.Extensions.DependencyInjection
//   2. IServiceCollection.AddSerilog(...)  — in Microsoft.Extensions.DependencyInjection
//      (added in recent versions of Serilog.AspNetCore alongside the host-builder wiring)
//
// P6 implements AddSerilog on ILoggingBuilder only
// (MMP.Herald.Serilog.AspNetCore.SerilogLoggingBuilderExtensions in Microsoft.Extensions.DependencyInjection).
// This Layer-2 file exposes the same surface by forwarding to that P6 method.
//
// P6 = MMP.Herald.Serilog.AspNetCore assembly (extern alias avoids the namespace collision).

using MMP.Herald.Pipeline;
using Microsoft.Extensions.Logging;
using P6Ext = P6::Microsoft.Extensions.DependencyInjection.SerilogLoggingBuilderExtensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods that wire a Herald <see cref="StructuredLogger"/> into
/// Microsoft.Extensions.Logging, mirroring the real Serilog
/// <c>SerilogLoggingBuilderExtensions.AddSerilog</c> surface.
/// Every member is a zero-logic forward to the P6 Layer-1 implementation.
/// </summary>
public static class SerilogServiceCollectionExtensions
{
    /// <summary>
    /// Adds a Herald logger provider to the MEL pipeline.
    /// Forwards to P6's <c>SerilogLoggingBuilderExtensions.AddSerilog</c>.
    /// </summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/> to configure.</param>
    /// <param name="logger">The Herald <see cref="StructuredLogger"/> to use.</param>
    /// <param name="dispose">Reserved for Serilog API parity — currently unused.</param>
    public static ILoggingBuilder AddSerilog(
        this ILoggingBuilder builder,
        StructuredLogger logger,
        bool dispose = false)
        => P6Ext.AddSerilog(builder, logger, dispose);
}
