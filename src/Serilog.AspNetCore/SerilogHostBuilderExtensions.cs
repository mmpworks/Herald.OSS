#nullable enable

// UseSerilog() wires a Herald StructuredLogger into the IHostBuilder / IHostApplicationBuilder
// pipeline so apps that use the standard ASP.NET Core or generic-host setup can swap
// their Serilog wiring for Herald with a single line change.
//
// Namespace is Microsoft.Extensions.Hosting so callers get the extension methods
// without an extra using statement — the same ergonomics as Serilog's own
// Serilog.Extensions.Hosting package.
//
// FM-7 mitigation (from the P6 pre-mortem):
//   SerilogLifetimeService registers Log.CloseAndFlush() on ApplicationStopped.
//   CloseAndFlush is idempotent (P1 R-5 pin) — a manual call at shutdown and
//   the ApplicationStopped hook firing together is a safe double-flush.

using System;
using System.Threading;
using System.Threading.Tasks;
using MMP.Herald.Addons.MelAdapter;
using MMP.Herald.Pipeline;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods that wire Herald into the .NET generic host as a drop-in
/// replacement for Serilog's <c>Serilog.AspNetCore</c> / <c>Serilog.Extensions.Hosting</c>
/// UseSerilog() call.
///
/// <para>
/// Three overloads mirror the Serilog surface:
/// <list type="bullet">
///   <item><see cref="UseSerilog(IHostBuilder,StructuredLogger)"/> — pre-built logger,
///   caller owns the lifetime.</item>
///   <item><see cref="UseSerilog(IHostBuilder,Action{HostBuilderContext,IServiceProvider,LoggerConfiguration})"/>
///   — lambda callback that receives the current <see cref="HostBuilderContext"/>,
///   the <see cref="IServiceProvider"/>, and a <see cref="LoggerConfiguration"/>
///   to configure before the host starts.</item>
///   <item><see cref="UseSerilog(IHostApplicationBuilder,StructuredLogger)"/> — modern
///   .NET 9+ WebApplication / generic-host builder surface.</item>
/// </list>
/// </para>
///
/// <para>
/// FM-7 (ApplicationStopped flush hook): all overloads register a
/// <see cref="SerilogLifetimeService"/> that calls <see cref="Log.CloseAndFlush"/>
/// when the host lifetime fires <c>ApplicationStopped</c>. The call is idempotent —
/// a second call from an app-level shutdown handler is a safe no-op.
/// </para>
/// </summary>
public static class SerilogHostBuilderExtensions
{
    // -------------------------------------------------------------------------
    // IHostBuilder overload 1: pre-built StructuredLogger
    // -------------------------------------------------------------------------

    /// <summary>
    /// Clears existing MEL providers and registers <paramref name="logger"/> via
    /// <see cref="SerilogLoggingBuilderExtensions.AddSerilog"/> so all
    /// <c>ILogger&lt;T&gt;</c> instances resolved from DI route through Herald.
    /// </summary>
    /// <param name="builder">The host builder to configure.</param>
    /// <param name="logger">
    ///   Pre-built <see cref="StructuredLogger"/>. The caller owns the lifetime;
    ///   the host will not dispose it, though the
    ///   <see cref="SerilogLifetimeService"/> calls
    ///   <see cref="Log.CloseAndFlush"/> on <c>ApplicationStopped</c>.
    /// </param>
    public static IHostBuilder UseSerilog(
        this IHostBuilder builder,
        StructuredLogger logger)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(logger);

        return builder.ConfigureServices((_, services) =>
        {
            services.AddLogging(b => b.ClearProviders().AddSerilog(logger));
            RegisterShutdownFlush(services);
        });
    }

    // -------------------------------------------------------------------------
    // IHostBuilder overload 2: lambda callback with LoggerConfiguration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Configures Herald via a <see cref="LoggerConfiguration"/> callback, then
    /// clears existing MEL providers and registers the resulting
    /// <see cref="StructuredLogger"/> with the DI container.
    ///
    /// <para>
    /// The lambda also sets <see cref="Log.Logger"/> to the configured adapter so
    /// that static <c>Log.Information(…)</c> calls work without DI. The
    /// <see cref="SerilogLifetimeService"/> flushes and resets it on
    /// <c>ApplicationStopped</c>.
    /// </para>
    /// </summary>
    /// <param name="builder">The host builder to configure.</param>
    /// <param name="configure">
    ///   Callback invoked at DI container build time. Receives the
    ///   <see cref="HostBuilderContext"/>, the <see cref="IServiceProvider"/>,
    ///   and a <see cref="LoggerConfiguration"/> to configure.
    /// </param>
    public static IHostBuilder UseSerilog(
        this IHostBuilder builder,
        Action<HostBuilderContext, IServiceProvider, LoggerConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        return builder.ConfigureServices((ctx, services) =>
        {
            // Defer the actual LoggerConfiguration build to the service-provider
            // factory phase so the callback has access to a fully-resolved
            // IServiceProvider. ConfigureServices runs before the provider is
            // built, so we register a factory delegate here and capture ctx.
            //
            // Cognitive complexity note: two-step factory pattern — register the
            // descriptor now, resolve + configure at first ILoggerProvider request.
            // The StructuredLogger produced here is stored on the singleton so the
            // DI container owns its scope for the host lifetime.

            services.AddSingleton<StructuredLogger>(sp =>
            {
                var lc = new LoggerConfiguration();
                configure(ctx, sp, lc);

                // Build the pipeline via SerilogLoggerAdapter.FromBuild so the adapter
                // owns the BootstrapResult (AsyncResource + SyncResources). Assigning
                // the adapter to Log.Logger means CloseAndFlush() will flush + dispose
                // it on ApplicationStopped (FM-7).
                //
                // The StructuredLogger extracted here is the raw engine logger that
                // MEL's HeraldLoggerProvider wraps for ILogger<T> calls.
                var adapter = SerilogLoggerAdapter.FromBuild(lc.Builder.Build());
                Log.Logger = adapter;
                return adapter.HeraldLogger;
            });

            services.AddLogging(b =>
            {
                b.ClearProviders();
                // Resolve the StructuredLogger singleton registered above so the
                // provider captures the same instance that Log.Logger wraps.
                b.Services.TryAddEnumerable(
                    ServiceDescriptor.Singleton<ILoggerProvider, HeraldLoggerProvider>(
                        sp => new HeraldLoggerProvider(sp.GetRequiredService<StructuredLogger>())));
            });

            RegisterShutdownFlush(services);
        });
    }

    // -------------------------------------------------------------------------
    // IHostApplicationBuilder overload (WebApplication.CreateBuilder() / net9+)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Clears existing MEL providers and registers <paramref name="logger"/> for
    /// use with <c>WebApplication.CreateBuilder()</c> and other
    /// <see cref="IHostApplicationBuilder"/> implementations.
    /// </summary>
    /// <param name="builder">The host application builder to configure.</param>
    /// <param name="logger">
    ///   Pre-built <see cref="StructuredLogger"/>. The caller owns the lifetime.
    /// </param>
    public static IHostApplicationBuilder UseSerilog(
        this IHostApplicationBuilder builder,
        StructuredLogger logger)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(logger);

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(logger);
        RegisterShutdownFlush(builder.Services);
        return builder;
    }

    // -------------------------------------------------------------------------
    // FM-7: ApplicationStopped flush registration
    // -------------------------------------------------------------------------

    // Registers the SerilogLifetimeService hosted service that hooks
    // Log.CloseAndFlush() to the ApplicationStopped cancellation token.
    // Internal so tests can verify the registered service type.
    internal static void RegisterShutdownFlush(IServiceCollection services)
    {
        services.AddSingleton<IHostedService, SerilogLifetimeService>();
    }
}

/// <summary>
/// Hosted service that calls <see cref="Log.CloseAndFlush"/> when
/// <see cref="IHostApplicationLifetime.ApplicationStopped"/> fires.
///
/// <para>
/// FM-7 mitigation: the registration is unconditional; a second
/// <see cref="Log.CloseAndFlush"/> call from an app-level handler is safe
/// because P1's Interlocked.Exchange design makes the method idempotent.
/// </para>
/// </summary>
internal sealed class SerilogLifetimeService : IHostedService
{
    private readonly IHostApplicationLifetime _lifetime;

    public SerilogLifetimeService(IHostApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Register the flush callback on ApplicationStopped (not Stopping).
        // ApplicationStopped fires after the host has signalled all stop
        // callbacks and the application has had a chance to process them —
        // the right point to flush and release the pipeline.
        _lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
