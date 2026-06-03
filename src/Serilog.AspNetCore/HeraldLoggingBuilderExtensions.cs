#nullable enable

// AddHerald wires a Herald pipeline into MEL ILoggingBuilder AND registers an
// ApplicationStopped flush so buffered logs drain on host shutdown.
//
// Why this exists (the shutdown-loss bug it fixes):
//   The bare provider swap, builder.AddProvider(new HeraldLoggerProvider(result.Logger)),
//   drops buffered events on host shutdown. HeraldLoggerProvider.Dispose is
//   _loggers.Clear(); it never flushes the pipeline. A synchronous WithConsoleSink
//   has no drain so the loss is invisible, but WithAsyncLogging or any buffered sink
//   loses everything still queued when the process exits.
//
//   AddHerald(QuickLogResult) closes the gap the same way the Serilog template does
//   (SerilogHostBuilderExtensions.RegisterShutdownFlush): a lifetime hosted service
//   flushes the pipeline on ApplicationStopped, after in-flight work drains, not on
//   ApplicationStopping.
//
// Namespace is Microsoft.Extensions.DependencyInjection so the consumer writes
//   builder.Logging.AddHerald(result)
// with no extra using, identical ergonomics to AddSerilog.

using System;
using System.Threading;
using System.Threading.Tasks;
using MMP.Herald.Addons.MelAdapter;
using MMP.Herald.Pipeline;
using MMP.Herald.Quick;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Wires a Herald pipeline into Microsoft.Extensions.Logging.
///
/// <para>
/// Two overloads, two lifetime stories:
/// <list type="bullet">
///   <item><see cref="AddHerald(ILoggingBuilder, QuickLogResult)"/> is the happy
///   path. Registers the MEL provider AND a lifetime hosted service that flushes
///   the pipeline on <c>ApplicationStopped</c>. Use this for hosted apps so buffered
///   logs survive shutdown.</item>
///   <item><see cref="AddHerald(ILoggingBuilder, StructuredLogger)"/> is for the
///   self-managed case. Registers only the provider; it does NOT auto-flush. The
///   caller owns the pipeline drain.</item>
/// </list>
/// </para>
/// </summary>
public static class HeraldLoggingBuilderExtensions
{
    /// <summary>
    /// Adds a <see cref="HeraldLoggerProvider"/> backed by <paramref name="result"/>
    /// AND registers a lifetime hosted service that flushes the Herald pipeline on
    /// <see cref="IHostApplicationLifetime.ApplicationStopped"/>.
    ///
    /// <para>
    /// The flush runs <see cref="QuickLogResult.DisposeAsync"/>, the same drain a
    /// consumer would run manually, so events still queued in an async or buffered
    /// sink reach their destination before the process exits. The flush is
    /// idempotent: a consumer that also calls <c>result.DisposeAsync()</c> on its own
    /// shutdown handler triggers a safe single drain, not a double-drain.
    /// </para>
    ///
    /// <para>
    /// Call <c>ClearProviders()</c> before this if you want Herald to be the sole
    /// provider (recommended for a clean single-pipeline host).
    /// </para>
    /// </summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/> to configure.</param>
    /// <param name="result">
    ///   The live pipeline from <c>QuickLogBuilder.BuildAndCommit()</c>. The lifetime
    ///   hosted service owns the shutdown flush of this result.
    /// </param>
    /// <returns>The <paramref name="builder"/> for chaining.</returns>
    public static ILoggingBuilder AddHerald(
        this ILoggingBuilder builder,
        QuickLogResult result)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(result);

        AddProvider(builder, result.Logger);
        RegisterShutdownFlush(builder.Services, result);
        return builder;
    }

    /// <summary>
    /// Adds a <see cref="HeraldLoggerProvider"/> backed by <paramref name="logger"/>.
    ///
    /// <para>
    /// <b>Does NOT auto-flush on shutdown.</b> This overload is for callers that own
    /// the pipeline lifetime themselves: they hold the <see cref="QuickLogResult"/>
    /// and call <c>DisposeAsync()</c> on their own shutdown path. If you want the host
    /// to flush buffered logs for you, use
    /// <see cref="AddHerald(ILoggingBuilder, QuickLogResult)"/> instead; a
    /// <see cref="StructuredLogger"/> alone has no handle to the pipeline async drain.
    /// </para>
    /// </summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/> to configure.</param>
    /// <param name="logger">The Herald logger that receives every MEL log call.</param>
    /// <returns>The <paramref name="builder"/> for chaining.</returns>
    public static ILoggingBuilder AddHerald(
        this ILoggingBuilder builder,
        StructuredLogger logger)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(logger);

        AddProvider(builder, logger);
        return builder;
    }

    // TryAddEnumerable matches on (ServiceType, ImplementationType), so two
    // AddHerald calls do not register two HeraldLoggerProvider descriptors.
    // Same FM-5 mitigation AddSerilog uses.
    private static void AddProvider(ILoggingBuilder builder, StructuredLogger logger)
    {
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, HeraldLoggerProvider>(
                _ => new HeraldLoggerProvider(logger)));
    }

    // Registers the HeraldLifetimeService that drains the pipeline on
    // ApplicationStopped. Internal so tests can assert the registered service type.
    // The factory resolves IHostApplicationLifetime from the container and pairs it
    // with the captured result.
    internal static void RegisterShutdownFlush(IServiceCollection services, QuickLogResult result)
    {
        services.AddSingleton<IHostedService>(sp =>
            new HeraldLifetimeService(
                sp.GetRequiredService<IHostApplicationLifetime>(),
                result));
    }
}

/// <summary>
/// Hosted service that flushes a Herald <see cref="QuickLogResult"/> when
/// <see cref="IHostApplicationLifetime.ApplicationStopped"/> fires.
///
/// <para>
/// Flush point is <c>ApplicationStopped</c> (not <c>Stopping</c>): it fires after the
/// host has run every stop callback and in-flight work has drained, which is the right
/// moment to flush the logging pipeline and release its resources.
/// </para>
///
/// <para>
/// Idempotent: an <see cref="Interlocked.Exchange(ref int, int)"/> guard ensures the
/// drain runs exactly once even when a consumer also calls
/// <see cref="QuickLogResult.DisposeAsync"/> on its own shutdown handler. The second
/// flush is a safe no-op rather than a double-drain.
/// </para>
/// </summary>
internal sealed class HeraldLifetimeService : IHostedService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly QuickLogResult _result;

    // 0 = not yet flushed, 1 = flushed. Interlocked.Exchange makes the transition
    // atomic so concurrent shutdown paths drain the pipeline exactly once.
    private int _flushed;

    public HeraldLifetimeService(IHostApplicationLifetime lifetime, QuickLogResult result)
    {
        _lifetime = lifetime;
        _result = result;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Register the flush on ApplicationStopped. The host fires the token
        // callbacks synchronously during shutdown; Flush blocks on the drain so the
        // pipeline is fully drained before the process exits. A fire-and-forget async
        // callback would let the process exit mid-drain, exactly the loss this fix
        // removes.
        _lifetime.ApplicationStopped.Register(Flush);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Drain the pipeline once, then flush stdout.
    //
    // Cognitive-complexity note: a single guarded path. Claim the flush, await the
    // async drain, flush Console.Out. GetAwaiter().GetResult() turns
    // QuickLogResult.DisposeAsync (a ValueTask) into a blocking drain so the
    // ApplicationStopped callback does not return until the pipeline is empty.
    // ConfigureAwait(false) avoids capturing a context the shutdown thread lacks.
    private void Flush()
    {
        // Claim the single flush. A racing manual DisposeAsync that already ran leaves
        // _flushed at 1, so this is a no-op; the inverse race is equally safe.
        if (Interlocked.Exchange(ref _flushed, 1) != 0)
            return;

        _result.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

        // Layer-B fix: a buffered console sink can leave bytes in the stdout buffer
        // even after the pipeline drains. Flush stdout unconditionally; it is a
        // sub-microsecond no-op when nothing is buffered, and skipping the
        // sink-detection walk keeps this method coupling-free and predictable.
        Console.Out.Flush();
    }
}
