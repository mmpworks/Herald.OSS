#nullable enable

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Pooling;
using MMP.Herald.Templating;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Interpolated string handler that gives <see cref="StructuredLogger"/> two
/// properties at once:
///
/// <list type="bullet">
/// <item><b>Zero-allocation rejection.</b> The handler's constructor calls
/// <see cref="StructuredLogger.IsEnabled"/> and writes the result into the
/// <c>out bool</c> parameter the compiler reads. When the level is below
/// minimum, the compiler skips every <c>AppendFormatted</c> call entirely —
/// no boxing, no template, no property list, no pool rent. The ref struct
/// itself lives on the stack.</item>
/// <item><b>Lazy allocation on acceptance, with template caching.</b> The
/// first accepted call at a given source location (file + line) builds the
/// template via a pooled <see cref="StringBuilder"/> and caches the result.
/// Every subsequent accepted call at the same site reuses the cached
/// template string — skipping StringBuilder rent, all literal appends, and
/// the final ToString allocation. On a typical hot path that repeats the
/// same <c>$"..."</c> call, this closes the accepted-path gap vs the manual
/// <c>LogProperty[]</c> API.</item>
/// </list>
///
/// <para>
/// <b>Property names come from the argument expression.</b> C#
/// <c>[CallerArgumentExpression]</c> captures the literal source text a caller
/// passed into a placeholder. <c>$"User {name}"</c> becomes a property named
/// <c>"name"</c>; <c>$"User {payload.UserName}"</c> becomes a property named
/// <c>"payload.UserName"</c>. Callers who want a short, stable property name
/// should assign to a local first: <c>var userName = payload.UserName;</c>.
/// This matches the naming convention ZLogger uses.
/// </para>
///
/// <para>
/// <b>Cache key.</b> The constructor receives
/// <see cref="CallerFilePathAttribute"/> and
/// <see cref="CallerLineNumberAttribute"/> values filled in by the C# compiler
/// at the call site of the interpolated string. We key the template cache on
/// a <c>(file, line)</c> tuple — unique per source position, stable across
/// calls, small enough to fit inside an int64 when we need it. The cache is
/// a process-global <see cref="ConcurrentDictionary{TKey, TValue}"/>; call
/// sites are bounded by the source tree, so the cache converges to a fixed
/// small size once every log call has fired once.
/// </para>
///
/// <para>
/// Cognitive-complexity note: the accepted-path fast path is 'cache hit →
/// skip literal appends, add properties, reuse cached template string'. The
/// cache-miss path behaves like the original implementation plus one cache
/// insert. Both branches are in <c>Drain</c>.
/// </para>
/// </summary>
[InterpolatedStringHandler]
public ref struct StructuredLogInterpolatedStringHandler
{
    // Process-global template cache. Keys are (CallerFilePath, CallerLineNumber)
    // which the compiler fills at the call site of the interpolated string.
    private static readonly ConcurrentDictionary<(string File, int Line), string> TemplateCache = new();

    private readonly bool _enabled;
    private readonly (string File, int Line) _siteKey;
    private readonly string? _cachedTemplate;
    private StringBuilder? _template;
    private List<LogPropertyCompact>? _properties;

    /// <summary>
    /// Constructor called by the C# compiler for <c>$"..."</c> at the call site
    /// of a method decorated with <c>[InterpolatedStringHandlerArgument("", "level")]</c>.
    /// Evaluates <see cref="StructuredLogger.IsEnabled"/> once and writes the
    /// result to <paramref name="shouldAppend"/> so the compiler can skip every
    /// subsequent <c>AppendFormatted</c> call on rejection. On acceptance,
    /// checks the template cache keyed by (file, line) and pre-allocates the
    /// property list sized to <paramref name="formattedCount"/> — one array
    /// alloc instead of the default List growth schedule.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StructuredLogInterpolatedStringHandler(
        int literalLength,
        int formattedCount,
        StructuredLogger logger,
        LogLevel level,
        out bool shouldAppend,
        [CallerFilePath] string? callerFilePath = null,
        [CallerLineNumber] int callerLineNumber = 0)
        : this(literalLength, formattedCount, logger.IsEnabled(level), out shouldAppend,
               callerFilePath, callerLineNumber)
    {
    }

    // Internal constructor that takes a pre-computed accept bool, so the
    // per-level typed handlers (Debug, Info, Warn, Error, Trace, Critical)
    // can feed their cached StructuredLogger.IsXxxAcceptable field in
    // directly — one bool-field read, no IsEnabled call, no dictionary
    // lookup, no interface dispatch on the reject path.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal StructuredLogInterpolatedStringHandler(
        int literalLength,
        int formattedCount,
        bool enabled,
        out bool shouldAppend,
        string? callerFilePath,
        int callerLineNumber)
    {
        _enabled = enabled;
        shouldAppend = enabled;

        // P1 reject-path optimization: assign every field to its default
        // first (cheap zero-stores the JIT usually folds into the ref-struct
        // stack frame setup), then early-return before the real work starts.
        // The tuple construction with the file-path coalesce and the cache
        // lookup only run on the accepted path.
        _siteKey = default;
        _template = null;
        _properties = null;
        _cachedTemplate = null;

        if (!_enabled) return;

        _siteKey = (callerFilePath ?? string.Empty, callerLineNumber);

        // Accepted path: try the template cache first. Cache hit skips every
        // AppendLiteral / StringBuilder rent. Cache miss rents the builder so
        // AppendLiteral never has to null-check.
        if (TemplateCache.TryGetValue(_siteKey, out var cached))
        {
            _cachedTemplate = cached;
        }
        else
        {
            _template = StringBuilderPool.Rent();
        }

        // Pre-size the property list from the compiler-supplied formattedCount.
        // No growth re-allocations during AppendFormatted. Rented from a
        // one-slot ThreadStatic pool so repeated accept calls on the same
        // thread don't pay the 40 B List + 24 B backing array over and over —
        // the list returns to the pool after DispatchFromHandler consumes it.
        _properties = formattedCount > 0 ? LogPropertyCompactListPool.Rent(formattedCount) : null;
    }

    /// <summary>
    /// True when the handler's constructor saw an accepted level.
    /// The owning Log method uses this as a defensive guard in case the
    /// compiler did not emit the <c>if (shouldAppend)</c> skip.
    /// </summary>
    public bool IsEnabled => _enabled;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendLiteral(string value)
    {
        // Cache hit short-circuit: the template is already built for this
        // site, so we drop every literal append on the floor.
        if (_cachedTemplate is not null) return;
        _template?.Append(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendFormatted<T>(
        T value,
        [CallerArgumentExpression("value")] string propertyName = "")
    {
        if (!_enabled) return;
        var name = string.IsNullOrEmpty(propertyName) ? "value" : propertyName;
        if (_cachedTemplate is null)
            _template!.Append('{').Append(name).Append('}');
        _properties!.Add(new LogPropertyCompact(name, value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendFormatted<T>(
        T value,
        string? format,
        [CallerArgumentExpression("value")] string propertyName = "")
    {
        if (!_enabled) return;
        var name = string.IsNullOrEmpty(propertyName) ? "value" : propertyName;
        if (_cachedTemplate is null)
        {
            var builder = _template!.Append('{').Append(name);
            if (!string.IsNullOrEmpty(format))
                builder.Append(':').Append(format);
            builder.Append('}');
        }
        // Format hint is dropped on the compact path. Rare in practice; a
        // caller that needs custom format strings per property falls back
        // to the LogProperty[] overload.
        _properties!.Add(new LogPropertyCompact(name, value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendFormatted(
        string? value,
        [CallerArgumentExpression("value")] string propertyName = "")
    {
        if (!_enabled) return;
        var name = string.IsNullOrEmpty(propertyName) ? "value" : propertyName;
        if (_cachedTemplate is null)
            _template!.Append('{').Append(name).Append('}');
        _properties!.Add(new LogPropertyCompact(name, value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendFormatted<T>(
        T value,
        int alignment,
        string? format = null,
        [CallerArgumentExpression("value")] string propertyName = "")
    {
        if (!_enabled) return;
        var name = string.IsNullOrEmpty(propertyName) ? "value" : propertyName;
        if (_cachedTemplate is null)
        {
            var builder = _template!.Append('{').Append(name);
            if (alignment != 0)
                builder.Append(',').Append(alignment);
            if (!string.IsNullOrEmpty(format))
                builder.Append(':').Append(format);
            builder.Append('}');
        }
        _properties!.Add(new LogPropertyCompact(name, value));
    }

    /// <summary>
    /// Called by <see cref="StructuredLogger"/> after the accepted-path guard.
    /// Returns the built (or cached) template string and the collected
    /// properties. On cache miss, writes the built template into the
    /// process-global cache so the next call at the same source location skips
    /// every literal append. Public so user code can build wrapper handler
    /// types for custom log levels — see the typed
    /// <c>InfoLogInterpolatedStringHandler</c> etc. for the pattern. Safe to
    /// call only when <see cref="IsEnabled"/> is true; on rejection the owning
    /// Log method short-circuits before reaching this method.
    ///
    /// <para>
    /// The returned <see cref="List{T}"/> is rented from
    /// <see cref="LogPropertyCompactListPool"/> and owned by the caller.
    /// <c>DispatchFromHandler</c> returns it to the pool after the
    /// synchronous sink dispatch completes. Safe because sinks copy
    /// any properties they need to persist before returning from
    /// <c>Log(in LogEventBuffer)</c> — the span backed by the list's
    /// storage is dead as soon as the sink returns.
    /// </para>
    /// </summary>
    public (string Template, IReadOnlyList<LogPropertyCompact>? Properties) Drain()
    {
        // Cache hit: the cached template is immutable; return it and the
        // property list we just built.
        if (_cachedTemplate is not null)
        {
            var props = _properties;
            _properties = null;
            return (_cachedTemplate, props);
        }

        // Cache miss: extract the built string, stash it in the cache so the
        // next call here skips all the append work.
        var built = _template is null
            ? string.Empty
            : StringBuilderPool.ReturnAndGetString(_template);
        _template = null;

        if (built.Length > 0)
            TemplateCache.TryAdd(_siteKey, built);

        var propsResult = _properties;
        _properties = null;
        return (built, propsResult);
    }
}
