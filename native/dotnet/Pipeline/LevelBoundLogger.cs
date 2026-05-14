#nullable enable

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Templating;

namespace MMP.Herald.Pipeline;

/// <summary>
/// A value-type handle that binds a <see cref="StructuredLogger"/> to a
/// specific <see cref="LogLevel"/>. Exists so callers can use the
/// interpolated-string-handler fast path with custom-registered levels
/// without writing a new handler type per level.
///
/// <para>
/// Registered once per level, reused forever — the struct is 16 bytes
/// (two references) and typically lives in a <c>static readonly</c> field.
/// Every call to <see cref="LogI"/> runs the same zero-allocation
/// rejection path as <see cref="StructuredLogger.InfoI"/>: the handler
/// constructor evaluates <see cref="StructuredLogger.IsEnabled"/>, writes
/// the result into <c>out shouldAppend</c>, and the compiler skips every
/// subsequent append on below-minimum calls.
/// </para>
///
/// <para>
/// Typical setup:
/// <code>
/// private static readonly LogLevel SecurityLevel =
///     LevelRegistry.Register("security", severity: 4);
///
/// private static readonly LevelBoundLogger Security =
///     herald.ForLevel(SecurityLevel);
///
/// Security.LogI(cat, $"Access denied for {userId} to {resource}");
/// </code>
/// </para>
///
/// <para>
/// Cognitive-complexity note: this type is a two-field value with one
/// public method. The method's body is the same three-step pattern used
/// by <see cref="StructuredLogger.InfoI"/> — early-return on rejection,
/// drain the handler, forward to the core <c>Log</c>. The ref-struct
/// handler handles the rest.
/// </para>
/// </summary>
public readonly struct LevelBoundLogger
{
    /// <summary>The underlying logger this handle dispatches into.</summary>
    public StructuredLogger Logger { get; }

    /// <summary>The log level baked into this handle.</summary>
    public LogLevel Level { get; }

    /// <summary>
    /// Pre-computed accept bool for <see cref="Level"/>, resolved once at
    /// binding time. The reject path on <see cref="LogI"/> becomes a single
    /// field read instead of a per-call <see cref="StructuredLogger.IsEnabled"/>
    /// lookup. Consistent with the per-known-level <c>IsXxxAcceptable</c>
    /// fields on <see cref="StructuredLogger"/>; the hot-reload story is the
    /// same (rebuild the logger → rebind via <c>ForLevel</c> → new accept bool).
    /// </summary>
    internal readonly bool IsAcceptable;

    internal LevelBoundLogger(StructuredLogger logger, LogLevel level)
    {
        Logger = logger;
        Level = level;
        IsAcceptable = logger.IsEnabled(level);
    }

    /// <summary>
    /// Interpolated-string log call bound to <see cref="Level"/>. Zero-alloc
    /// rejection when <see cref="StructuredLogger.IsEnabled"/> returns false;
    /// lazy template + property allocation on accept.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void LogI(
        LogCategory category,
        [InterpolatedStringHandlerArgument("")]
        ref LevelBoundLogInterpolatedStringHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, properties) = handler.Drain();

        // Compact-span dispatch, same pattern as StructuredLogger's typed
        // handlers. Zero per-property allocation on the kernel path.
        if (properties is null or { Count: 0 })
        {
            Logger.LogCompact(Level, category, template,
                System.ReadOnlySpan<Kernel.LogPropertyCompact>.Empty);
            return;
        }

        // try/finally so a throwing sink still returns the list to the
        // pool — leaks here compound across accept calls until the thread
        // is recycled. Mirrors StructuredLogger.DispatchFromHandler.
        if (properties is List<Kernel.LogPropertyCompact> list)
        {
            try
            {
                Logger.LogCompact(Level, category, template,
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list));
            }
            finally
            {
                Kernel.LogPropertyCompactListPool.Return(list);
            }
            return;
        }

        var array = new Kernel.LogPropertyCompact[properties.Count];
        for (var i = 0; i < properties.Count; i++)
        {
            array[i] = properties[i];
        }
        Logger.LogCompact(Level, category, template, array);
    }
}

/// <summary>
/// Interpolated-string handler for <see cref="LevelBoundLogger.LogI"/>.
/// Mirrors <see cref="StructuredLogInterpolatedStringHandler"/>'s API so the
/// ctor pulls logger + level straight out of the bound handle.
/// </summary>
[InterpolatedStringHandler]
public ref struct LevelBoundLogInterpolatedStringHandler
{
    /// <summary>
    /// Public readonly field so the caller's <c>if (!handler.IsEnabled) return;</c>
    /// is a direct load. Mirrors the typed per-level handlers.
    /// </summary>
    public readonly bool IsEnabled;

    private StructuredLogInterpolatedStringHandler _inner;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LevelBoundLogInterpolatedStringHandler(
        int literalLength,
        int formattedCount,
        LevelBoundLogger bound,
        out bool shouldAppend,
        [CallerFilePath] string? callerFilePath = null,
        [CallerLineNumber] int callerLineNumber = 0)
    {
        // Reject path uses the accept bool baked into the bound logger at
        // binding time — no FrozenDictionary lookup per call, no inner
        // ctor entered on reject.
        IsEnabled = bound.IsAcceptable;
        if (!IsEnabled)
        {
            _inner = default;
            shouldAppend = false;
            return;
        }

        _inner = new StructuredLogInterpolatedStringHandler(
            literalLength, formattedCount, enabled: true,
            out shouldAppend, callerFilePath, callerLineNumber);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendLiteral(string value) => _inner.AppendLiteral(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendFormatted<T>(T value,
        [CallerArgumentExpression("value")] string propertyName = "")
        => _inner.AppendFormatted(value, propertyName);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendFormatted<T>(T value, string? format,
        [CallerArgumentExpression("value")] string propertyName = "")
        => _inner.AppendFormatted(value, format, propertyName);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendFormatted(string? value,
        [CallerArgumentExpression("value")] string propertyName = "")
        => _inner.AppendFormatted(value, propertyName);

    public (string Template, IReadOnlyList<Kernel.LogPropertyCompact>? Properties) Drain()
        => _inner.Drain();
}
