#nullable enable

// Layer-2 Serilog.Log mirror.
//
// CRIT-FM-L2 — No backing field.
//   The mutable slot lives exclusively in MMP.Herald.Serilog.Log._logger (Layer 1).
//   This class holds NO additional volatile field. Every read and write goes
//   straight through to L1.Log.Logger. Any Layer-2 or Layer-1 call that touches
//   the slot sees the same value — there is only one slot.
//
// Logger get path:
//   Wraps the L1 ILogger in a Core.Logger (the concrete Serilog.Core.Logger type
//   that corpus code expects). A new wrapper is created on each property read —
//   Log.Logger is a startup-time call, not a hot path, so the allocation is fine.
//
// Logger set path:
//   If the supplied Serilog.ILogger is a Core.Logger, extract its inner L1.ILogger
//   to avoid a double-wrap (Core.Logger → L1 → Core.Logger chain). Otherwise store
//   it directly: any Serilog.ILogger also implements MMP.Herald.Serilog.ILogger
//   because both interfaces have identical method signatures and this assembly's
//   Serilog.ILogger is defined using MMP.Herald.Serilog.Events types.

using System;
using System.Runtime.CompilerServices;
using L1 = MMP.Herald.Serilog;

namespace Serilog;

/// <summary>
/// Static ambient logger — drop-in replacement for Serilog's <c>Log</c> class.
///
/// <para>
/// All state lives in <see cref="L1.Log"/>. This class is a pure forwarding
/// layer: every member delegates to the Layer-1 equivalent with no additional
/// logic or mutable fields.
/// </para>
/// </summary>
public static class Log
{
    // ── Logger slot (NO backing field — CRIT-FM-L2) ───────────────────────────

    /// <summary>
    /// The ambient logger. Reads and writes forward directly to
    /// <see cref="L1.Log.Logger"/> — this property owns no slot of its own.
    /// </summary>
    public static ILogger Logger
    {
        get
        {
            // Wrap the L1 logger in the concrete Layer-2 type that corpus code
            // stores (Serilog.Core.Logger). A fresh wrapper per read is
            // acceptable — Log.Logger is a startup-path accessor, not a hot path.
            return new Core.Logger(L1.Log.Logger);
        }
        set
        {
            // null → L1 setter's own null-guard resets to SilentLogger.Instance.
            if (value is null)
            {
                L1.Log.Logger = null!;
                return;
            }

            // Unwrap Core.Logger to extract the L1.ILogger it wraps (avoids a
            // double-wrap in the L1 slot). For any L1.ILogger that a caller has
            // already placed in the L1 slot and then read back via Layer-2
            // (e.g. the TestLoggers pattern), this recovers the original adapter.
            //
            // Direct L1.ILogger implementations (SerilogLoggerAdapter, SilentLogger)
            // are also accepted via the as-cast. A Serilog.ILogger that is neither
            // a Core.Logger nor an L1.ILogger is a runtime configuration error.
            if (value is Core.Logger concrete)
            {
                L1.Log.Logger = concrete.Inner;
            }
            else if (value is L1.ILogger direct)
            {
                L1.Log.Logger = direct;
            }
            else
            {
                throw new InvalidCastException(
                    $"Cannot assign {value.GetType().FullName} to MMP.Herald.Serilog.Log.Logger. " +
                    "Use a MMP.Herald.Serilog.SerilogLoggerAdapter or a Serilog.Core.Logger.");
            }
        }
    }

    // ── Verbs (no exception) ──────────────────────────────────────────────────

    /// <inheritdoc cref="L1.Log.Verbose(string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Verbose(string messageTemplate, params object?[]? propertyValues)
        => L1.Log.Verbose(messageTemplate, propertyValues);

    /// <inheritdoc cref="L1.Log.Debug(string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Debug(string messageTemplate, params object?[]? propertyValues)
        => L1.Log.Debug(messageTemplate, propertyValues);

    /// <inheritdoc cref="L1.Log.Information(string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Information(string messageTemplate, params object?[]? propertyValues)
        => L1.Log.Information(messageTemplate, propertyValues);

    /// <inheritdoc cref="L1.Log.Warning(string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Warning(string messageTemplate, params object?[]? propertyValues)
        => L1.Log.Warning(messageTemplate, propertyValues);

    /// <inheritdoc cref="L1.Log.Error(string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Error(string messageTemplate, params object?[]? propertyValues)
        => L1.Log.Error(messageTemplate, propertyValues);

    /// <inheritdoc cref="L1.Log.Fatal(string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Fatal(string messageTemplate, params object?[]? propertyValues)
        => L1.Log.Fatal(messageTemplate, propertyValues);

    // ── Verbs (with exception) ────────────────────────────────────────────────

    /// <inheritdoc cref="L1.Log.Verbose(Exception?, string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Verbose(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => L1.Log.Verbose(exception, messageTemplate, propertyValues);

    /// <inheritdoc cref="L1.Log.Debug(Exception?, string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Debug(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => L1.Log.Debug(exception, messageTemplate, propertyValues);

    /// <inheritdoc cref="L1.Log.Information(Exception?, string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Information(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => L1.Log.Information(exception, messageTemplate, propertyValues);

    /// <inheritdoc cref="L1.Log.Warning(Exception?, string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Warning(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => L1.Log.Warning(exception, messageTemplate, propertyValues);

    /// <inheritdoc cref="L1.Log.Error(Exception?, string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Error(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => L1.Log.Error(exception, messageTemplate, propertyValues);

    /// <inheritdoc cref="L1.Log.Fatal(Exception?, string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Fatal(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => L1.Log.Fatal(exception, messageTemplate, propertyValues);

    // ── Write ─────────────────────────────────────────────────────────────────

    // Note: Write and IsEnabled use MMP.Herald.Serilog.Events.LogEventLevel (the L1
    // type, aliased as L1.Events) so calls stay type-safe without an enum cast.
    // Serilog.Events.LogEventLevel (the Layer-2 compat enum) has the same ordinal
    // values; corpus code that passes it can use an explicit cast: (L1.Events.LogEventLevel)level.

    /// <inheritdoc cref="L1.Log.Write(MMP.Herald.Serilog.Events.LogEventLevel, string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write(L1.Events.LogEventLevel level, string messageTemplate, params object?[]? propertyValues)
        => L1.Log.Write(level, messageTemplate, propertyValues);

    /// <inheritdoc cref="L1.Log.Write(MMP.Herald.Serilog.Events.LogEventLevel, Exception?, string, object?[]?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write(L1.Events.LogEventLevel level, Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => L1.Log.Write(level, exception, messageTemplate, propertyValues);

    // ── Level gate ────────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsEnabled(L1.Events.LogEventLevel level)
        => L1.Log.Logger.IsEnabled(level);

    // ── Context ───────────────────────────────────────────────────────────────

    /// <inheritdoc cref="L1.Log.ForContext(string, object?, bool)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ILogger ForContext(string propertyName, object? value, bool destructureObjects = false)
        => new Core.Logger(L1.Log.ForContext(propertyName, value, destructureObjects));

    /// <inheritdoc cref="L1.Log.ForContext{TSource}()"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ILogger ForContext<TSource>()
        => new Core.Logger(L1.Log.ForContext<TSource>());

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <inheritdoc cref="L1.Log.CloseAndFlush"/>
    public static void CloseAndFlush() => L1.Log.CloseAndFlush();
}
