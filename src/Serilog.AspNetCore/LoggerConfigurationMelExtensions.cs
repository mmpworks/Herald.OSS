#nullable enable

// W5 — the LoggerConfiguration → MEL bridge.
//
// The shipped AddSerilog(StructuredLogger) overload (SerilogLoggingBuilderExtensions)
// wires a raw Herald logger into MEL. This overload lets the *compat* config —
// a Serilog-shaped LoggerConfiguration built via WriteTo/MinimumLevel/Enrich —
// reach the same seam, so a consumer who migrated their Serilog bootstrap can do:
//
//     builder.Logging.AddSerilog(new LoggerConfiguration().WriteTo.Console());
//
// Namespace is Microsoft.Extensions.DependencyInjection (same as the
// StructuredLogger overload) so no extra using is needed at the call site —
// identical ergonomics to Serilog's own AddSerilog(LoggerConfiguration).

using System;
using Microsoft.Extensions.Logging;
using MMP.Herald.Serilog;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Bridges a Serilog-shaped <see cref="LoggerConfiguration"/> into
/// Microsoft.Extensions.Logging, mirroring Serilog's
/// <c>AddSerilog(this ILoggingBuilder, LoggerConfiguration)</c>.
/// </summary>
public static class LoggerConfigurationMelExtensions
{
    /// <summary>
    /// Build the pipeline described by <paramref name="config"/> and register it
    /// with MEL as the logging provider.
    ///
    /// <para>
    /// The build is memoized on the <see cref="LoggerConfiguration"/>: calling
    /// <see cref="LoggerConfiguration.CreateLogger"/> as well as this method on
    /// the same configuration yields two views over one pipeline, never two
    /// pipelines. This method delegates to the shipped
    /// <see cref="SerilogLoggingBuilderExtensions.AddSerilog(ILoggingBuilder, MMP.Herald.Pipeline.StructuredLogger, bool)"/>
    /// overload via <see cref="LoggerConfiguration.CreateHeraldLogger"/>.
    /// </para>
    /// </summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/> to configure.</param>
    /// <param name="config">
    ///   The Serilog-shaped configuration describing the pipeline. Its
    ///   <c>WriteTo</c>/<c>MinimumLevel</c>/<c>Enrich</c> state must be set before
    ///   this call — the pipeline is built here.
    /// </param>
    /// <param name="dispose">
    ///   Forwarded to the underlying <c>AddSerilog(StructuredLogger)</c> overload
    ///   for Serilog API parity. See that overload for current lifetime semantics.
    /// </param>
    /// <returns>The <paramref name="builder"/> for chaining.</returns>
    public static ILoggingBuilder AddSerilog(
        this ILoggingBuilder builder,
        LoggerConfiguration config,
        bool dispose = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(config);

        return builder.AddSerilog(config.CreateHeraldLogger(), dispose);
    }
}
