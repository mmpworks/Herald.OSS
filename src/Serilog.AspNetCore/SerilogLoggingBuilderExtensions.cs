#nullable enable

// AddSerilog() wires HeraldLoggerProvider into MEL's ILoggingBuilder so
// any ILogger<T> resolved from DI routes through Herald's pipeline.
//
// Namespace is Microsoft.Extensions.DependencyInjection so the consumer
// writes:  services.AddLogging(b => b.AddSerilog(heraldLogger))
// without a new using statement — identical ergonomics to Serilog's own
// MicrosoftExtensionsLogging compat package.

using System;
using MMP.Herald.Addons.MelAdapter;
using MMP.Herald.Pipeline;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods that wire a Herald <see cref="StructuredLogger"/> into
/// Microsoft.Extensions.Logging as a drop-in replacement for Serilog's
/// <c>Serilog.Extensions.Logging</c> AddSerilog() call.
///
/// <para>
/// FM-5 mitigation: <see cref="TryAddEnumerable"/> is used instead of
/// <see cref="ServiceCollectionDescriptorExtensions.Add"/> so that calling
/// <c>AddSerilog</c> twice with the same logger instance does not register
/// two providers. One Herald provider per DI container.
/// </para>
/// </summary>
public static class SerilogLoggingBuilderExtensions
{
    /// <summary>
    /// Adds a <see cref="HeraldLoggerProvider"/> backed by <paramref name="logger"/>
    /// to the logging pipeline. Call <c>ClearProviders()</c> before this if you
    /// want Herald to be the sole provider (recommended for Serilog drop-in use).
    /// </summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/> to configure.</param>
    /// <param name="logger">
    ///   The Herald <see cref="StructuredLogger"/> that will receive every
    ///   MEL log call. The caller owns the logger lifetime; this method does
    ///   not dispose it.
    /// </param>
    /// <param name="dispose">
    ///   Reserved for Serilog API parity. Currently unused — Herald logger
    ///   lifetime is always managed by the caller. Will be honored in a
    ///   future release when IHostedService shutdown integration is added.
    /// </param>
    /// <returns>The <paramref name="builder"/> for chaining.</returns>
    public static ILoggingBuilder AddSerilog(
        this ILoggingBuilder builder,
        StructuredLogger logger,
        bool dispose = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(logger);

        // TryAddEnumerable: registers the descriptor only when no equivalent
        // descriptor already exists for ILoggerProvider. "Equivalent" is
        // matched on (ServiceType, ImplementationType) — so two calls with
        // different StructuredLogger instances still both register (different
        // implementation instances have the same ImplementationType
        // HeraldLoggerProvider). This is the correct FM-5 mitigation for the
        // same-provider-type double-registration scenario.
        //
        // C# 12 note: using ServiceDescriptor.Singleton factory overload so
        // the descriptor captures the concrete instance without a separate
        // Singleton<T>(instance) overload that may not exist in all MEL
        // versions we target (net9/net10).
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, HeraldLoggerProvider>(
                _ => new HeraldLoggerProvider(logger)));

        return builder;
    }

    /// <summary>
    /// Register a fully-built Serilog-shaped <see cref="MMP.Herald.Serilog.ILogger"/>
    /// as the MEL logging provider. Mirrors Serilog's
    /// <c>AddSerilog(this ILoggingBuilder, Serilog.ILogger logger, bool dispose)</c>:
    /// a migrated <c>l.AddSerilog(new LoggerConfiguration()...CreateLogger())</c> call
    /// resolves here.
    ///
    /// <para>
    /// The logger must be a Herald-built adapter (the product of
    /// <c>LoggerConfiguration.CreateLogger()</c>); its underlying Herald
    /// <see cref="StructuredLogger"/> is unwrapped and handed to the
    /// <c>AddSerilog(StructuredLogger)</c> overload so MEL log calls flow through
    /// the same pipeline. A logger from any other <see cref="MMP.Herald.Serilog.ILogger"/>
    /// implementation cannot be bridged into MEL and throws
    /// <see cref="ArgumentException"/> with an actionable message.
    /// </para>
    /// </summary>
    /// <param name="builder">The MEL logging builder.</param>
    /// <param name="logger">
    /// The Serilog-shaped logger from <c>CreateLogger()</c>. Must not be null.
    /// </param>
    /// <param name="dispose">Forwarded for Serilog API parity (see the StructuredLogger overload).</param>
    public static ILoggingBuilder AddSerilog(
        this ILoggingBuilder builder,
        MMP.Herald.Serilog.ILogger logger,
        bool dispose = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(logger);

        if (logger is not MMP.Herald.Serilog.SerilogLoggerAdapter adapter)
        {
            throw new ArgumentException(
                "AddSerilog(ILogger) requires a logger produced by " +
                "LoggerConfiguration.CreateLogger(). A custom ILogger implementation " +
                "cannot be bridged into Microsoft.Extensions.Logging.",
                nameof(logger));
        }

        return builder.AddSerilog(adapter.HeraldLogger, dispose);
    }
}
