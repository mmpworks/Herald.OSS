#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Templating;

// (Intentionally import Pipeline for ILogScope / StructuredLogger interop.)

namespace MMP.Herald;

/// <summary>
/// Concrete <see cref="ILogger{T}"/> built on top of a single shared
/// <see cref="StructuredLogger"/>. Every method is a one-liner forwarder
/// that supplies the cached <see cref="LogCategory"/> derived from
/// <typeparamref name="T"/>.
///
/// <para>
/// The class is <c>internal</c>: callers obtain instances through
/// <see cref="TypedLoggerFactory"/> or the
/// <see cref="QuickLogResultExtensions.CreateTypedLogger{T}"/> helper. Keeping
/// the constructor non-public lets the factory enforce that every typed
/// logger shares the same underlying pipeline.
/// </para>
/// </summary>
internal sealed class TypedLogger<T> : ILogger<T>
{
    // Closed generics get their own static field, so the category lookup
    // happens exactly once per T regardless of how many TypedLogger<T>
    // instances exist. typeof(T).Name avoids the namespace-bearing FullName
    // because category strings are operator-facing and short forms read better
    // in dashboards and grep output. Operators that need disambiguation
    // between same-named types in different namespaces should resolve through
    // their own naming convention (e.g. wrap in a sealed class per bounded
    // context) rather than push namespaces into log lines.
    private static readonly LogCategory _category = new(typeof(T).Name);

    private readonly StructuredLogger _inner;

    internal TypedLogger(StructuredLogger inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public LogCategory Category => _category;

    public bool IsEnabled(LogLevel level) => _inner.IsEnabled(level);

    public void Trace(string messageTemplate, params LogProperty[] properties) =>
        _inner.Trace(_category, messageTemplate, properties: ToList(properties));

    public void Debug(string messageTemplate, params LogProperty[] properties) =>
        _inner.Debug(_category, messageTemplate, properties: ToList(properties));

    public void Info(string messageTemplate, params LogProperty[] properties) =>
        _inner.Info(_category, messageTemplate, properties: ToList(properties));

    public void Warn(string messageTemplate, params LogProperty[] properties) =>
        _inner.Warn(_category, messageTemplate, properties: ToList(properties));

    public void Error(string messageTemplate, params LogProperty[] properties) =>
        _inner.Error(_category, messageTemplate, properties: ToList(properties));

    public void Trace(Exception exception, string messageTemplate, params LogProperty[] properties) =>
        _inner.Trace(_category, exception, messageTemplate, properties: ToList(properties));

    public void Debug(Exception exception, string messageTemplate, params LogProperty[] properties) =>
        _inner.Debug(_category, exception, messageTemplate, properties: ToList(properties));

    public void Info(Exception exception, string messageTemplate, params LogProperty[] properties) =>
        _inner.Info(_category, exception, messageTemplate, properties: ToList(properties));

    public void Warn(Exception exception, string messageTemplate, params LogProperty[] properties) =>
        _inner.Warn(_category, exception, messageTemplate, properties: ToList(properties));

    public void Error(Exception exception, string messageTemplate, params LogProperty[] properties) =>
        _inner.Error(_category, exception, messageTemplate, properties: ToList(properties));

    public ILogger<T> WithContext(IReadOnlyDictionary<string, object?> defaultContext)
    {
        ArgumentNullException.ThrowIfNull(defaultContext);
        // StructuredLogger.WithContext returns a new StructuredLogger that
        // shares the pipeline but holds a merged default-context snapshot.
        // Wrapping it keeps the typed facade's static category cache intact;
        // the new TypedLogger<T> and the old one share the same per-T
        // LogCategory without re-deriving it.
        return new TypedLogger<T>(_inner.WithContext(defaultContext));
    }

    public ILogScope BeginScope(IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return _inner.BeginScope(values);
    }

    // StructuredLogger's typed methods accept IReadOnlyList<LogProperty>?.
    // params LogProperty[] is the natural caller-side shape; null/empty stays
    // null so the underlying pipeline keeps its zero-allocation fast path
    // (events with no properties bypass the property-list copy in
    // LogEventFactory.Create).
    private static IReadOnlyList<LogProperty>? ToList(LogProperty[] properties) =>
        properties is null || properties.Length == 0 ? null : properties;
}
