#nullable enable

// Layer-2 Serilog.ILogger mirror.
//
// Design:
//   - Method signatures use MMP.Herald.Serilog.Events.LogEventLevel (aliased
//     as L1Events) so that Serilog.Core.Logger — which wraps an L1.ILogger
//     and delegates straight through — compiles without any cast at every
//     forwarding call site.
//   - The Layer-2 Serilog.Events.LogEventLevel enum (same ordinals) is
//     separately available for corpus code that names the type explicitly.
//     Serilog.Core.Logger provides an (int)-pivot cast in its implementation
//     when callers use the L2 enum type in their own code.
//   - Zero logic lives here. Every declared member is a contract; all
//     implementation resides in Layer-1 or in Layer-2 adapter wrappers.

using System;
using L1Events = MMP.Herald.Serilog.Events;

namespace Serilog;

/// <summary>
/// Drop-in replacement for <c>Serilog.ILogger</c>.
/// Mirrors the public surface of <see cref="MMP.Herald.Serilog.ILogger"/>
/// under the <c>Serilog</c> namespace so existing Serilog call sites
/// compile without modification.
/// </summary>
public interface ILogger
{
    // ── Level gate ────────────────────────────────────────────────────────────

    /// <summary>Returns true when <paramref name="level"/> passes the pipeline floor.</summary>
    bool IsEnabled(L1Events.LogEventLevel level);

    // ── Write ─────────────────────────────────────────────────────────────────

    void Write(L1Events.LogEventLevel level, string messageTemplate, params object?[]? propertyValues);
    void Write(L1Events.LogEventLevel level, Exception? exception, string messageTemplate, params object?[]? propertyValues);

    // ── Verb overloads (no exception) ─────────────────────────────────────────

    void Verbose(string messageTemplate, params object?[]? propertyValues);
    void Debug(string messageTemplate, params object?[]? propertyValues);
    void Information(string messageTemplate, params object?[]? propertyValues);
    void Warning(string messageTemplate, params object?[]? propertyValues);
    void Error(string messageTemplate, params object?[]? propertyValues);
    void Fatal(string messageTemplate, params object?[]? propertyValues);

    // ── Verb overloads (with exception) ───────────────────────────────────────

    void Verbose(Exception? exception, string messageTemplate, params object?[]? propertyValues);
    void Debug(Exception? exception, string messageTemplate, params object?[]? propertyValues);
    void Information(Exception? exception, string messageTemplate, params object?[]? propertyValues);
    void Warning(Exception? exception, string messageTemplate, params object?[]? propertyValues);
    void Error(Exception? exception, string messageTemplate, params object?[]? propertyValues);
    void Fatal(Exception? exception, string messageTemplate, params object?[]? propertyValues);

    // ── Context ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Return a new logger that attaches <paramref name="value"/> under
    /// <paramref name="propertyName"/> to every subsequent event.
    /// </summary>
    ILogger ForContext(string propertyName, object? value, bool destructureObjects = false);

    /// <summary>Return a new logger sourced from <typeparamref name="TSource"/>.</summary>
    ILogger ForContext<TSource>();

    /// <summary>Return a new logger sourced from <paramref name="source"/>.</summary>
    ILogger ForContext(Type source);
}
