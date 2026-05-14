#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Templating;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Readonly-struct wrapper that binds a <see cref="StructuredLogger"/>
/// to a <see cref="LogCategory"/>. Serilog users reach for
/// <c>ForContext&lt;T&gt;()</c> first; <see cref="CategoryBoundLogger"/>
/// is Herald's equivalent — a logger that carries its category so
/// callers don't pass it on every log line.
/// </summary>
/// <remarks>
/// <para>
/// The type is a <see cref="System.Runtime.CompilerServices.IsReadOnlyAttribute">
/// readonly</see> struct so caching one in a <c>static readonly</c>
/// field at class scope is free — no heap alloc, no GC pressure,
/// identity copies are cheap. Methods delegate straight to the
/// underlying <see cref="StructuredLogger"/>, so there's no behaviour
/// drift vs calling the logger directly.
/// </para>
/// <para>
/// Interpolated call sites (<c>InfoI</c>, <c>DebugI</c>, etc.) are
/// re-exposed directly on the struct. The level-typed handlers carry
/// a second ctor that accepts <see cref="CategoryBoundLogger"/> and
/// delegates to the <see cref="StructuredLogger"/> ctor, so the
/// reject-path shape is identical — one bool load out of the bound
/// logger's precomputed acceptance field.
/// </para>
/// <code>
/// private static readonly CategoryBoundLogger Log = logger.ForCategory&lt;UserService&gt;();
///
/// Log.Info("User {Name} signed in",
///     properties: new[] { new LogProperty("Name", name) });
///
/// Log.InfoFast("User {Name} signed in", ("Name", name));
///
/// Log.InfoI($"User {name} signed in");  // zero-alloc reject, category implicit
/// </code>
/// </remarks>
public readonly struct CategoryBoundLogger
{
    public StructuredLogger Logger { get; }
    public LogCategory Category { get; }

    internal CategoryBoundLogger(StructuredLogger logger, LogCategory category)
    {
        Logger = logger;
        Category = category;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsEnabled(LogLevel level) => Logger.IsEnabled(level);

    // ── Simple typed methods ─────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Trace(string messageTemplate, IReadOnlyList<LogProperty>? properties = null) =>
        Logger.Log(KnownLogLevels.Trace, Category, messageTemplate, properties);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Debug(string messageTemplate, IReadOnlyList<LogProperty>? properties = null) =>
        Logger.Log(KnownLogLevels.Debug, Category, messageTemplate, properties);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Info(string messageTemplate, IReadOnlyList<LogProperty>? properties = null) =>
        Logger.Log(KnownLogLevels.Info, Category, messageTemplate, properties);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Warn(string messageTemplate, IReadOnlyList<LogProperty>? properties = null) =>
        Logger.Log(KnownLogLevels.Warn, Category, messageTemplate, properties);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Error(string messageTemplate, IReadOnlyList<LogProperty>? properties = null) =>
        Logger.Log(KnownLogLevels.Error, Category, messageTemplate, properties);

    // ── Fast-path tuples (arity 1..4) ────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void InfoFast(string template,
        (string Name, object? Value) p1) =>
        Logger.InfoFast(Category, template, p1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void InfoFast(string template,
        (string Name, object? Value) p1, (string Name, object? Value) p2) =>
        Logger.InfoFast(Category, template, p1, p2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void InfoFast(string template,
        (string Name, object? Value) p1, (string Name, object? Value) p2,
        (string Name, object? Value) p3) =>
        Logger.InfoFast(Category, template, p1, p2, p3);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void InfoFast(string template,
        (string Name, object? Value) p1, (string Name, object? Value) p2,
        (string Name, object? Value) p3, (string Name, object? Value) p4) =>
        Logger.InfoFast(Category, template, p1, p2, p3, p4);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WarnFast(string template, (string Name, object? Value) p1) =>
        Logger.WarnFast(Category, template, p1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WarnFast(string template,
        (string Name, object? Value) p1, (string Name, object? Value) p2) =>
        Logger.WarnFast(Category, template, p1, p2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ErrorFast(string template, (string Name, object? Value) p1) =>
        Logger.ErrorFast(Category, template, p1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ErrorFast(string template,
        (string Name, object? Value) p1, (string Name, object? Value) p2) =>
        Logger.ErrorFast(Category, template, p1, p2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DebugFast(string template, (string Name, object? Value) p1) =>
        Logger.DebugFast(Category, template, p1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TraceFast(string template, (string Name, object? Value) p1) =>
        Logger.TraceFast(Category, template, p1);

    // ── Interpolated-string overloads (zero-alloc reject) ────────
    //
    // These wire into the same level-typed handlers StructuredLogger
    // uses. The handler has a second ctor that accepts
    // CategoryBoundLogger and forwards to the StructuredLogger ctor,
    // so reject-path shape matches StructuredLogger.InfoI exactly:
    // one bool load out of bound.Logger.IsXxxAcceptable, and every
    // compiler-emitted AppendFormatted short-circuits via shouldAppend.

    /// <summary>Trace-level interpolated-string overload bound to this category.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TraceI(
        [InterpolatedStringHandlerArgument("")]
        ref TraceLogInterpolatedStringHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, properties) = handler.Drain();
        Logger.DispatchFromHandler(KnownLogLevels.Trace, Category, template, properties);
    }

    /// <summary>Debug-level interpolated-string overload bound to this category.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DebugI(
        [InterpolatedStringHandlerArgument("")]
        ref DebugLogInterpolatedStringHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, properties) = handler.Drain();
        Logger.DispatchFromHandler(KnownLogLevels.Debug, Category, template, properties);
    }

    /// <summary>Info-level interpolated-string overload bound to this category.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void InfoI(
        [InterpolatedStringHandlerArgument("")]
        ref InfoLogInterpolatedStringHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, properties) = handler.Drain();
        Logger.DispatchFromHandler(KnownLogLevels.Info, Category, template, properties);
    }

    /// <summary>Warn-level interpolated-string overload bound to this category.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WarnI(
        [InterpolatedStringHandlerArgument("")]
        ref WarnLogInterpolatedStringHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, properties) = handler.Drain();
        Logger.DispatchFromHandler(KnownLogLevels.Warn, Category, template, properties);
    }

    /// <summary>Error-level interpolated-string overload bound to this category.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ErrorI(
        [InterpolatedStringHandlerArgument("")]
        ref ErrorLogInterpolatedStringHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, properties) = handler.Drain();
        Logger.DispatchFromHandler(KnownLogLevels.Error, Category, template, properties);
    }
}

/// <summary>
/// Extension methods on <see cref="StructuredLogger"/> that port
/// Serilog idioms into Herald shape.
/// </summary>
public static class StructuredLoggerDxExtensions
{
    /// <summary>
    /// Bind a logger to the category string <c>typeof(T).FullName</c>.
    /// Cache the result in a <c>static readonly</c> field at class
    /// scope — the returned struct is a zero-alloc handle.
    /// </summary>
    public static CategoryBoundLogger ForCategory<T>(this StructuredLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        var category = new LogCategory(typeof(T).FullName ?? typeof(T).Name);
        return new CategoryBoundLogger(logger, category);
    }

    /// <summary>
    /// Bind a logger to an explicit category string.
    /// </summary>
    public static CategoryBoundLogger ForCategory(this StructuredLogger logger, string categoryName)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);
        return new CategoryBoundLogger(logger, new LogCategory(categoryName));
    }

    /// <summary>
    /// Bind a logger to a pre-constructed <see cref="LogCategory"/>.
    /// Preferred for hot paths because the category object is
    /// reused rather than reconstructed at every call site.
    /// </summary>
    public static CategoryBoundLogger ForCategory(this StructuredLogger logger, LogCategory category)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(category);
        return new CategoryBoundLogger(logger, category);
    }

    /// <summary>
    /// Push one ambient property onto the logger's scope stack.
    /// Dispose the returned scope to pop. One-liner equivalent of
    /// Serilog's <c>LogContext.PushProperty</c> — mirrors the idiom
    /// Serilog users reach for first.
    /// </summary>
    /// <example>
    /// <code>
    /// using (logger.PushProperty("RequestId", requestId))
    /// {
    ///     logger.Info(AppCategory, "processing");  // carries RequestId
    /// }
    /// </code>
    /// </example>
    public static ILogScope PushProperty(
        this StructuredLogger logger, string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [name] = value
        };
        return logger.BeginScope(values);
    }
}
