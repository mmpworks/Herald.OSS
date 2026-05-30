#nullable enable

// Serilog.Core.Logger — the concrete type that corpus code stores:
//   Logger log = new LoggerConfiguration()....CreateLogger();
//
// Layer-1 LoggerConfiguration.CreateLogger() returns MMP.Herald.Serilog.ILogger
// (an interface). This class wraps that interface result so the Layer-2
// CreateLogger() can return a concrete Serilog.Core.Logger, matching the
// real Serilog API where Logger is a sealed class not an interface.
//
// It is a wrapper, not a cast — the Layer-1 result is stored in _inner
// and every ILogger member is forwarded through it.
//
// Interface type contract:
//   Serilog.ILogger (Layer-2, ILogger.cs) declares IsEnabled/Write using
//   MMP.Herald.Serilog.Events.LogEventLevel (the L1 type, aliased as L1Events).
//   Logger must implement those methods using the same L1 type.
//   Since _inner also uses L1Events.LogEventLevel, every forward is a straight
//   pass-through with no conversion required.

using System;
using System.Runtime.CompilerServices;
using L1 = MMP.Herald.Serilog;
using L1Events = MMP.Herald.Serilog.Events;

namespace Serilog.Core;

/// <summary>
/// The concrete logger type returned by <c>LoggerConfiguration.CreateLogger()</c>.
/// Wraps the Layer-1 <see cref="L1.ILogger"/> produced by
/// <c>MMP.Herald.Serilog.Configuration.LoggerConfiguration.CreateLogger()</c>.
///
/// <para>
/// CRIT-FM-G2 confirmed: Layer-1's <c>CreateLogger()</c> returns
/// <c>MMP.Herald.Serilog.ILogger</c> (an interface). This class is a wrapper
/// construction, not a cast.
/// </para>
/// </summary>
public sealed class Logger : Serilog.ILogger, IDisposable
{
    private readonly L1.ILogger _inner;

    internal Logger(L1.ILogger inner) => _inner = inner;

    /// <summary>
    /// Exposes the wrapped Layer-1 logger. Used by <c>Serilog.Log</c> to avoid
    /// a double-wrap when assigning to the ambient slot.
    /// </summary>
    internal L1.ILogger Inner => _inner;

    // ── Level gate ────────────────────────────────────────────────────────────
    // ILogger.IsEnabled uses L1Events.LogEventLevel — forward directly, no cast.

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsEnabled(L1Events.LogEventLevel level) => _inner.IsEnabled(level);

    // ── Write ─────────────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(L1Events.LogEventLevel level, string messageTemplate, params object?[]? propertyValues)
        => _inner.Write(level, messageTemplate, propertyValues);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(L1Events.LogEventLevel level, Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => _inner.Write(level, exception, messageTemplate, propertyValues);

    // ── Verbs (no exception) ──────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Verbose(string messageTemplate, params object?[]? propertyValues)
        => _inner.Verbose(messageTemplate, propertyValues);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Debug(string messageTemplate, params object?[]? propertyValues)
        => _inner.Debug(messageTemplate, propertyValues);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Information(string messageTemplate, params object?[]? propertyValues)
        => _inner.Information(messageTemplate, propertyValues);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Warning(string messageTemplate, params object?[]? propertyValues)
        => _inner.Warning(messageTemplate, propertyValues);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Error(string messageTemplate, params object?[]? propertyValues)
        => _inner.Error(messageTemplate, propertyValues);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Fatal(string messageTemplate, params object?[]? propertyValues)
        => _inner.Fatal(messageTemplate, propertyValues);

    // ── Verbs (with exception) ────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Verbose(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => _inner.Verbose(exception, messageTemplate, propertyValues);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Debug(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => _inner.Debug(exception, messageTemplate, propertyValues);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Information(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => _inner.Information(exception, messageTemplate, propertyValues);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Warning(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => _inner.Warning(exception, messageTemplate, propertyValues);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Error(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => _inner.Error(exception, messageTemplate, propertyValues);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Fatal(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => _inner.Fatal(exception, messageTemplate, propertyValues);

    // ── Context ───────────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Serilog.ILogger ForContext(string propertyName, object? value, bool destructureObjects = false)
        => new Logger(_inner.ForContext(propertyName, value, destructureObjects));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Serilog.ILogger ForContext<TSource>()
        => new Logger(_inner.ForContext<TSource>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Serilog.ILogger ForContext(Type source)
        => new Logger(_inner.ForContext(source));

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Disposes the underlying Herald pipeline if it implements
    /// <see cref="IDisposable"/>. Safe to call multiple times — the
    /// Layer-1 logger owns the idempotency guarantee.
    /// </summary>
    public void Dispose() => (_inner as IDisposable)?.Dispose();
}
