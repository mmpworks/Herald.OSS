#nullable enable

using System.Runtime.CompilerServices;
using MMP.Herald.Levels;

namespace MMP.Herald.Pipeline;

// -----------------------------------------------------------------------
// Typed interpolated-string handlers — one per common log level.
//
// Each wrapper pre-bakes the level into the constructor call so typed
// convenience methods (Info / Debug / Warn / Error / Trace / Fatal) can
// use InterpolatedStringHandlerArgument("") with only the logger, and the
// caller does not have to pass the level themselves.
//
// All wrappers delegate to the shared StructuredLogInterpolatedStringHandler
// so the accepted-path work (template caching by source location, property
// collection, pool management) stays in one place. The wrappers are ref
// structs with one bool and one inner struct — the JIT inlines every append
// method straight through.
//
// Reject-path note:
// the wrapper's ctor checks the logger's pre-computed IsXxxAcceptable field
// FIRST and skips the inner ctor entirely when rejected. That drops the
// zero-stores and the enabled-branch inside the inner ctor from the reject
// path. The wrapper's own `_inner` field is marked via Unsafe.SkipInit, so
// the compiler's definite-assignment check passes without emitting the 5
// zero-stores that `_inner = default` would otherwise produce. The rejected
// call is now a single bool-field read into a public readonly field plus
// `shouldAppend = false`. Every append emitted by the compiler is dead on
// a reject because shouldAppend=false short-circuits the whole `$"..."`
// expansion.
//
// IsEnabled is a public readonly field (not a property) so the caller's
// `if (!handler.IsEnabled) return;` is a direct load instead of a property
// getter that reads through `_inner._enabled`.
//
// Cognitive-complexity note: the wrappers intentionally look identical. The
// level constant is the only difference. Splitting by level (instead of
// parameterizing one type) is what lets C# bind [InterpolatedStringHandlerArgument("")]
// to a single "this" argument on the Info / Debug / ... methods.
// -----------------------------------------------------------------------

/// <summary>Level-typed handler for <c>StructuredLogger.TraceI(cat, $"...")</c>.</summary>
[InterpolatedStringHandler]
public ref struct TraceLogInterpolatedStringHandler
{
    /// <summary>
    /// True when the logger's minimum level allowed a Trace call through.
    /// Direct field so callers pay a single load on reject — no property
    /// indirection to the inner handler.
    /// </summary>
    public readonly bool IsEnabled;

    private StructuredLogInterpolatedStringHandler _inner;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TraceLogInterpolatedStringHandler(
        int literalLength, int formattedCount,
        StructuredLogger logger, out bool shouldAppend,
        [CallerFilePath] string? callerFilePath = null,
        [CallerLineNumber] int callerLineNumber = 0)
    {
        IsEnabled = logger.IsTraceAcceptable;
        if (!IsEnabled)
        {
            // Reject-path: on net9.0+ Unsafe.SkipInit gained the
            // `allows ref struct` relaxation, so we use it to satisfy the
            // compiler's definite-assignment check without emitting the 5
            // zero-stores that `_inner = default` otherwise produces. On
            // net8.0 the ref-struct constraint blocks SkipInit, so we fall
            // back to the explicit zero-store. The inner struct is never
            // read on reject (DebugI et al. guard with !handler.IsEnabled
            // before calling Drain), so leaving its bytes uninitialised is
            // safe.
#if NET9_0_OR_GREATER
            Unsafe.SkipInit(out _inner);
#else
            _inner = default;
#endif
            shouldAppend = false;
            return;
        }

        _inner = new StructuredLogInterpolatedStringHandler(
            literalLength, formattedCount, enabled: true,
            out shouldAppend, callerFilePath, callerLineNumber);
    }

    /// <summary>
    /// Bound-logger overload. Picked by the compiler when the containing
    /// method's <c>this</c> is <see cref="CategoryBoundLogger"/>. Same
    /// reject-path shape: one bool load out of <c>bound.Logger</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TraceLogInterpolatedStringHandler(
        int literalLength, int formattedCount,
        CategoryBoundLogger bound, out bool shouldAppend,
        [CallerFilePath] string? callerFilePath = null,
        [CallerLineNumber] int callerLineNumber = 0)
        : this(literalLength, formattedCount, bound.Logger,
               out shouldAppend, callerFilePath, callerLineNumber) { }

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

    internal (string Template, System.Collections.Generic.IReadOnlyList<Kernel.LogPropertyCompact>? Properties) Drain()
        => _inner.Drain();
}

/// <summary>Level-typed handler for <c>StructuredLogger.DebugI(cat, $"...")</c>.</summary>
[InterpolatedStringHandler]
public ref struct DebugLogInterpolatedStringHandler
{
    public readonly bool IsEnabled;
    private StructuredLogInterpolatedStringHandler _inner;

    /// <summary>Bound-logger overload. See Trace variant for rationale.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DebugLogInterpolatedStringHandler(
        int literalLength, int formattedCount,
        CategoryBoundLogger bound, out bool shouldAppend,
        [CallerFilePath] string? callerFilePath = null,
        [CallerLineNumber] int callerLineNumber = 0)
        : this(literalLength, formattedCount, bound.Logger,
               out shouldAppend, callerFilePath, callerLineNumber) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DebugLogInterpolatedStringHandler(
        int literalLength, int formattedCount,
        StructuredLogger logger, out bool shouldAppend,
        [CallerFilePath] string? callerFilePath = null,
        [CallerLineNumber] int callerLineNumber = 0)
    {
        IsEnabled = logger.IsDebugAcceptable;
        if (!IsEnabled)
        {
            // Reject-path: on net9.0+ Unsafe.SkipInit gained the
            // `allows ref struct` relaxation, so we use it to satisfy the
            // compiler's definite-assignment check without emitting the 5
            // zero-stores that `_inner = default` otherwise produces. On
            // net8.0 the ref-struct constraint blocks SkipInit, so we fall
            // back to the explicit zero-store. The inner struct is never
            // read on reject (DebugI et al. guard with !handler.IsEnabled
            // before calling Drain), so leaving its bytes uninitialised is
            // safe.
#if NET9_0_OR_GREATER
            Unsafe.SkipInit(out _inner);
#else
            _inner = default;
#endif
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

    internal (string Template, System.Collections.Generic.IReadOnlyList<Kernel.LogPropertyCompact>? Properties) Drain()
        => _inner.Drain();
}

/// <summary>Level-typed handler for <c>StructuredLogger.InfoI(cat, $"...")</c>.</summary>
[InterpolatedStringHandler]
public ref struct InfoLogInterpolatedStringHandler
{
    public readonly bool IsEnabled;
    private StructuredLogInterpolatedStringHandler _inner;

    /// <summary>Bound-logger overload. See Trace variant for rationale.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public InfoLogInterpolatedStringHandler(
        int literalLength, int formattedCount,
        CategoryBoundLogger bound, out bool shouldAppend,
        [CallerFilePath] string? callerFilePath = null,
        [CallerLineNumber] int callerLineNumber = 0)
        : this(literalLength, formattedCount, bound.Logger,
               out shouldAppend, callerFilePath, callerLineNumber) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public InfoLogInterpolatedStringHandler(
        int literalLength, int formattedCount,
        StructuredLogger logger, out bool shouldAppend,
        [CallerFilePath] string? callerFilePath = null,
        [CallerLineNumber] int callerLineNumber = 0)
    {
        IsEnabled = logger.IsInfoAcceptable;
        if (!IsEnabled)
        {
            // Reject-path: on net9.0+ Unsafe.SkipInit gained the
            // `allows ref struct` relaxation, so we use it to satisfy the
            // compiler's definite-assignment check without emitting the 5
            // zero-stores that `_inner = default` otherwise produces. On
            // net8.0 the ref-struct constraint blocks SkipInit, so we fall
            // back to the explicit zero-store. The inner struct is never
            // read on reject (DebugI et al. guard with !handler.IsEnabled
            // before calling Drain), so leaving its bytes uninitialised is
            // safe.
#if NET9_0_OR_GREATER
            Unsafe.SkipInit(out _inner);
#else
            _inner = default;
#endif
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

    internal (string Template, System.Collections.Generic.IReadOnlyList<Kernel.LogPropertyCompact>? Properties) Drain()
        => _inner.Drain();
}

/// <summary>Level-typed handler for <c>StructuredLogger.WarnI(cat, $"...")</c>.</summary>
[InterpolatedStringHandler]
public ref struct WarnLogInterpolatedStringHandler
{
    public readonly bool IsEnabled;
    private StructuredLogInterpolatedStringHandler _inner;

    /// <summary>Bound-logger overload. See Trace variant for rationale.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WarnLogInterpolatedStringHandler(
        int literalLength, int formattedCount,
        CategoryBoundLogger bound, out bool shouldAppend,
        [CallerFilePath] string? callerFilePath = null,
        [CallerLineNumber] int callerLineNumber = 0)
        : this(literalLength, formattedCount, bound.Logger,
               out shouldAppend, callerFilePath, callerLineNumber) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WarnLogInterpolatedStringHandler(
        int literalLength, int formattedCount,
        StructuredLogger logger, out bool shouldAppend,
        [CallerFilePath] string? callerFilePath = null,
        [CallerLineNumber] int callerLineNumber = 0)
    {
        IsEnabled = logger.IsWarnAcceptable;
        if (!IsEnabled)
        {
            // Reject-path: on net9.0+ Unsafe.SkipInit gained the
            // `allows ref struct` relaxation, so we use it to satisfy the
            // compiler's definite-assignment check without emitting the 5
            // zero-stores that `_inner = default` otherwise produces. On
            // net8.0 the ref-struct constraint blocks SkipInit, so we fall
            // back to the explicit zero-store. The inner struct is never
            // read on reject (DebugI et al. guard with !handler.IsEnabled
            // before calling Drain), so leaving its bytes uninitialised is
            // safe.
#if NET9_0_OR_GREATER
            Unsafe.SkipInit(out _inner);
#else
            _inner = default;
#endif
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

    internal (string Template, System.Collections.Generic.IReadOnlyList<Kernel.LogPropertyCompact>? Properties) Drain()
        => _inner.Drain();
}

/// <summary>Level-typed handler for <c>StructuredLogger.ErrorI(cat, $"...")</c>.</summary>
[InterpolatedStringHandler]
public ref struct ErrorLogInterpolatedStringHandler
{
    public readonly bool IsEnabled;
    private StructuredLogInterpolatedStringHandler _inner;

    /// <summary>Bound-logger overload. See Trace variant for rationale.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ErrorLogInterpolatedStringHandler(
        int literalLength, int formattedCount,
        CategoryBoundLogger bound, out bool shouldAppend,
        [CallerFilePath] string? callerFilePath = null,
        [CallerLineNumber] int callerLineNumber = 0)
        : this(literalLength, formattedCount, bound.Logger,
               out shouldAppend, callerFilePath, callerLineNumber) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ErrorLogInterpolatedStringHandler(
        int literalLength, int formattedCount,
        StructuredLogger logger, out bool shouldAppend,
        [CallerFilePath] string? callerFilePath = null,
        [CallerLineNumber] int callerLineNumber = 0)
    {
        IsEnabled = logger.IsErrorAcceptable;
        if (!IsEnabled)
        {
            // Reject-path: on net9.0+ Unsafe.SkipInit gained the
            // `allows ref struct` relaxation, so we use it to satisfy the
            // compiler's definite-assignment check without emitting the 5
            // zero-stores that `_inner = default` otherwise produces. On
            // net8.0 the ref-struct constraint blocks SkipInit, so we fall
            // back to the explicit zero-store. The inner struct is never
            // read on reject (DebugI et al. guard with !handler.IsEnabled
            // before calling Drain), so leaving its bytes uninitialised is
            // safe.
#if NET9_0_OR_GREATER
            Unsafe.SkipInit(out _inner);
#else
            _inner = default;
#endif
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

    internal (string Template, System.Collections.Generic.IReadOnlyList<Kernel.LogPropertyCompact>? Properties) Drain()
        => _inner.Drain();
}
