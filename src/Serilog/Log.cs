#nullable enable

// Static ambient-singleton facade (compat layer ONLY; Herald core stays DI-first).
//
// Design (R-1 pin, Open Decision 1):
//   - Log.Logger lives in this Layer-1 assembly behind a volatile holder.
//   - No separate StaticFacade assembly.
//   - Thread safety: Volatile.Read/Write for the slot; Interlocked.Exchange for CloseAndFlush.
//   - SilentLogger.Instance is the ambient default — calls before any assignment are silent no-ops.
//
// CloseAndFlush idempotency (R-5 pin):
//   - Interlocked.Exchange atomically swaps the current logger with SilentLogger.Instance.
//   - The swapped-out logger is disposed exactly once by this call.
//   - Subsequent CloseAndFlush calls exchange SilentLogger.Instance back to itself — IDisposable
//     is not implemented by SilentLogger, so the cast returns null and no double-dispose occurs.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog;

/// <summary>
/// Static ambient logger — drop-in equivalent of Serilog's <c>Log</c> class.
///
/// <para>
/// Before any assignment <see cref="Logger"/> is a <see cref="SilentLogger"/> so
/// calls are safe no-ops. Assign a configured <see cref="SerilogLoggerAdapter"/>
/// (or any <see cref="ILogger"/>) at application startup, then call
/// <see cref="CloseAndFlush"/> on shutdown to flush the pipeline and reset to
/// the silent default.
/// </para>
///
/// <para>
/// This class is intentionally a thin forwarding layer over <see cref="Logger"/>.
/// All policy (level gating, context enrichment, pipeline disposal) lives in the
/// assigned logger and its Herald pipeline. The static class itself holds no
/// mutable state beyond the logger slot.
/// </para>
/// </summary>
public static class Log
{
    // Volatile field: writes from any thread are immediately visible to reads
    // on other threads without needing a full lock. Volatile semantics are
    // sufficient here because CloseAndFlush uses Interlocked.Exchange (which
    // implies a full memory barrier) and Logger assignment is a single-word
    // store that is already atomic on all supported architectures.
    //
    // C# 12 note: kept as a plain static field for .NET compatibility.
    // A future C# / runtime version may offer a dedicated volatile-reference
    // primitive that conveys intent more clearly; revisit when available.
    private static ILogger _logger = SilentLogger.Instance;

    /// <summary>
    /// The ambient logger. Defaults to <see cref="SilentLogger.Instance"/> until
    /// set by the application. Setting to <c>null</c> resets to the silent default.
    /// Thread-safe for both reads and writes.
    /// </summary>
    public static ILogger Logger
    {
        get => Volatile.Read(ref _logger);
        set => Volatile.Write(ref _logger, value ?? SilentLogger.Instance);
    }

    // -------------------------------------------------------------------------
    // Verb forwarding — all delegate to Logger for zero coupling to the slot.
    // AggressiveInlining: the JIT can eliminate the indirection on hot paths
    // once Logger is monomorphic (the common case after startup).
    // -------------------------------------------------------------------------

    /// <inheritdoc cref="ILogger.Verbose(string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Verbose(string messageTemplate, params object?[]? propertyValues)
        => Logger.Verbose(messageTemplate, propertyValues);

    /// <inheritdoc cref="ILogger.Debug(string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Debug(string messageTemplate, params object?[]? propertyValues)
        => Logger.Debug(messageTemplate, propertyValues);

    /// <inheritdoc cref="ILogger.Information(string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Information(string messageTemplate, params object?[]? propertyValues)
        => Logger.Information(messageTemplate, propertyValues);

    /// <inheritdoc cref="ILogger.Warning(string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Warning(string messageTemplate, params object?[]? propertyValues)
        => Logger.Warning(messageTemplate, propertyValues);

    /// <inheritdoc cref="ILogger.Error(string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Error(string messageTemplate, params object?[]? propertyValues)
        => Logger.Error(messageTemplate, propertyValues);

    /// <inheritdoc cref="ILogger.Fatal(string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Fatal(string messageTemplate, params object?[]? propertyValues)
        => Logger.Fatal(messageTemplate, propertyValues);

    // -------------------------------------------------------------------------
    // Exception-bearing verb forwarding
    // -------------------------------------------------------------------------

    /// <inheritdoc cref="ILogger.Verbose(Exception?, string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Verbose(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => Logger.Verbose(exception, messageTemplate, propertyValues);

    /// <inheritdoc cref="ILogger.Debug(Exception?, string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Debug(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => Logger.Debug(exception, messageTemplate, propertyValues);

    /// <inheritdoc cref="ILogger.Information(Exception?, string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Information(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => Logger.Information(exception, messageTemplate, propertyValues);

    /// <inheritdoc cref="ILogger.Warning(Exception?, string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Warning(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => Logger.Warning(exception, messageTemplate, propertyValues);

    /// <inheritdoc cref="ILogger.Error(Exception?, string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Error(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => Logger.Error(exception, messageTemplate, propertyValues);

    /// <inheritdoc cref="ILogger.Fatal(Exception?, string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Fatal(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => Logger.Fatal(exception, messageTemplate, propertyValues);

    // -------------------------------------------------------------------------
    // Write (generic entry point — mirrors Serilog's Log.Write)
    // -------------------------------------------------------------------------

    /// <inheritdoc cref="ILogger.Write(Events.LogEventLevel, string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write(Events.LogEventLevel level, string messageTemplate, params object?[]? propertyValues)
        => Logger.Write(level, messageTemplate, propertyValues);

    /// <inheritdoc cref="ILogger.Write(Events.LogEventLevel, Exception?, string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write(Events.LogEventLevel level, Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => Logger.Write(level, exception, messageTemplate, propertyValues);

    // -------------------------------------------------------------------------
    // Context enrichment
    // -------------------------------------------------------------------------

    /// <inheritdoc cref="ILogger.ForContext(string, object?, bool)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ILogger ForContext(string propertyName, object? value, bool destructureObjects = false)
        => Logger.ForContext(propertyName, value, destructureObjects);

    /// <inheritdoc cref="ILogger.ForContext{TSource}()"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ILogger ForContext<TSource>()
        => Logger.ForContext<TSource>();

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Flushes the current logger if it implements <see cref="IDisposable"/> (i.e.
    /// it was created via <see cref="SerilogLoggerAdapter.FromBuild"/> and owns the
    /// pipeline lifetime), then resets <see cref="Logger"/> to the silent default.
    ///
    /// <para>
    /// Idempotent: the Interlocked.Exchange swap ensures the old logger is disposed
    /// exactly once regardless of how many threads call this simultaneously.
    /// After the swap, <see cref="Logger"/> is <see cref="SilentLogger.Instance"/>,
    /// which does not implement <see cref="IDisposable"/>, so every subsequent call
    /// is a safe no-op.
    /// </para>
    /// </summary>
    public static void CloseAndFlush()
    {
        // Atomic swap: grab whatever is current and plant SilentLogger.Instance.
        // Any racing CloseAndFlush sees SilentLogger and its cast to IDisposable
        // returns null — no double-dispose.
        var prior = Interlocked.Exchange(ref _logger, SilentLogger.Instance);
        (prior as IDisposable)?.Dispose();
    }
}
