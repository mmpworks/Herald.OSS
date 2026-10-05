#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Enrichers;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Pooling;
using MMP.Herald.Quick;
using MMP.Herald.Templating;
using MMP.Herald.Time;

namespace MMP.Herald.Events;

/// <summary>
/// Default event factory.
/// Keeps time access, scope capture, enrichment, and template rendering out of the rest of the pipeline.
/// </summary>
public sealed class LogEventFactory : ILogEventFactory
{
    /// <summary>
    /// Default cap on the message-template string length in UTF-16 characters.
    /// A caller passing a 1 GiB template could OOM the process before the
    /// filter chain even ran; the cap converts that into a dropped-event
    /// signal instead. 64 KiB is well above any legitimate structured
    /// message.
    /// </summary>
    public const int DefaultMaxTemplateBytes = 64 * 1024;

    /// <summary>
    /// Default cap on the sum of stringified property-value lengths.
    /// 256 KiB is generous for structured logs — a single value that large
    /// almost always indicates serialising something that should be
    /// summarised (a binary blob, an entire HTTP body).
    /// </summary>
    public const int DefaultMaxPropertyValueBytes = 256 * 1024;

    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly MessageTemplateParser _messageTemplateParser;
    private readonly ILogScopeProvider _scopeProvider;
    private readonly ILogEnricher _enricher;
    private readonly int _maxTemplateBytes;
    private readonly int _maxPropertyValueBytes;
    private readonly Action<DropReason>? _onDropped;

    // Cached at ctor so the fast-path check on every Create call is a single
    // bool load instead of an `is` type test. Flips to true when the caller
    // configured no enricher chain — the common case on a QuickLogBuilder
    // with the new precomputed-default-context pattern.
    private readonly bool _enricherIsNoOp;

    public LogEventFactory(
        IDateTimeProvider dateTimeProvider,
        MessageTemplateParser messageTemplateParser,
        ILogScopeProvider scopeProvider,
        ILogEnricher enricher,
        int maxTemplateBytes = DefaultMaxTemplateBytes,
        int maxPropertyValueBytes = DefaultMaxPropertyValueBytes,
        Action<DropReason>? onDropped = null)
    {
        _dateTimeProvider = dateTimeProvider;
        _messageTemplateParser = messageTemplateParser;
        _scopeProvider = scopeProvider;
        _enricher = enricher;
        _maxTemplateBytes = maxTemplateBytes > 0 ? maxTemplateBytes : DefaultMaxTemplateBytes;
        _maxPropertyValueBytes = maxPropertyValueBytes > 0 ? maxPropertyValueBytes : DefaultMaxPropertyValueBytes;
        _onDropped = onDropped;
        _enricherIsNoOp = enricher is Enrichers.NullLogEnricher;
    }

    public LogEvent Create(
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        IReadOnlyList<LogProperty>? properties = null,
        IReadOnlyDictionary<string, object?>? defaultContext = null,
        IReadOnlyDictionary<string, object?>? context = null,
        LogEventId? eventId = null)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(messageTemplate);

        // Size caps at the API boundary. Earlier than any enricher or filter
        // so a pathological caller cannot drive work further down the
        // pipeline. When either cap trips we invoke the drop callback with
        // DropReason.OversizedEvent and return a tiny placeholder — the
        // pipeline keeps flowing, the offending payload does not.
        var (propertyBytes, toStringFailures) = ComputePropertyByteCount(properties);
        if (messageTemplate.Length > _maxTemplateBytes || propertyBytes > _maxPropertyValueBytes)
        {
            _onDropped?.Invoke(DropReason.OversizedEvent);
            return new LogEvent(
                TimeUtc: _dateTimeProvider.GetUtcNow(),
                Level: level,
                Category: category,
                MessageTemplate: "[event rejected: oversized]",
                Message: "[event rejected: oversized]",
                Properties: LogEvent.EmptyProperties,
                Context: LogEvent.EmptyContext,
                EventId: eventId,
                TenantId: HeraldTenantScope.Current);
        }

        // A value whose ToString throws would throw again in the renderer or in any formatter downstream.
        // The size check above already found it. Its first failure decides: the value is replaced with fallback
        // text (FallbackText) under every property that references the same object, and ToString is never called
        // on it again, so an intermittent ToString cannot pass. Formatters guard their own ToString as a second
        // layer, because any object can throw on a later call (see FallbackText.ValueText).
        if (toStringFailures is not null) properties = WithFailedValuesReplaced(properties!, toStringFailures);

        // Fast path: if no enrichers, no scope, and no caller context,
        // skip pooled collection rent/return entirely. Saves ~100-200ns.
        // The enricher check is cached at ctor; the scope check short-
        // circuits on a null AsyncLocal before allocating.
        if (_enricherIsNoOp
            && (context is null or { Count: 0 }))
        {
            var scopeCtx = _scopeProvider.GetMergedContext();
            if (scopeCtx is null or { Count: 0 })
                return CreateFastPath(level, category, messageTemplate, properties, defaultContext, eventId);
        }

        // Standard path: rent pooled collections for enrichment context.
        var pooledProps = CollectionPool.RentPropertyList();
        var pooledCtx = CollectionPool.RentContextDictionary();

        if (properties is not null)
        {
            for (var i = 0; i < properties.Count; i++)
            {
                pooledProps.Add(properties[i]);
            }
        }

        MergeContextInto(pooledCtx, defaultContext);
        MergeContextInto(pooledCtx, _scopeProvider.GetMergedContext());
        MergeContextInto(pooledCtx, context);

        var enrichmentContext = new LogEventEnrichmentContext(
            level, category, messageTemplate, pooledProps, pooledCtx, pooled: true);

        _enricher.Enrich(enrichmentContext);

        // L2 of the async-sink cross-tenant PII fix (0.10.2): walk every
        // property after enrichers run and BEFORE the renderer reads them.
        // Lazy Func<object?> factories invoke here, on the producer thread,
        // while the original AsyncLocal / HttpContext / tenant scope is
        // still in effect. PiiSensitive properties additionally force-to-
        // string. Without this, a Func that captures HttpContextAccessor or
        // an AsyncLocal would read the WRONG tenant's value when the async
        // sink eventually resolved it on the drain thread.
        LogPropertyEagerResolver.ResolveInPlace(pooledProps);

        RenderedMessage renderedMessage;
        try
        {
            renderedMessage = _messageTemplateParser.Render(
                messageTemplate,
                enrichmentContext.Properties);
        }
        catch (Exception ex)
        {
            // Template parsing/rendering failed. Produce a safe fallback so the
            // pipeline keeps flowing. The raw template and the exception type
            // are kept (never the message: see FallbackText) - the event is degraded but not lost.
            renderedMessage = new RenderedMessage(
                Template: messageTemplate,
                Message: FallbackText.TemplateError(ex, messageTemplate),
                Properties: enrichmentContext.Properties);
        }

        // Create the frozen context for the immutable LogEvent.
        // Optimization: if no enricher modified the context AND no scoped context was merged,
        // we can reuse the caller's original context (or EmptyContext) without copying.
        var frozenContext = CreateFrozenContext(enrichmentContext);

        var logEvent = new LogEvent(
            TimeUtc: _dateTimeProvider.GetUtcNow(),
            Level: level,
            Category: category,
            MessageTemplate: renderedMessage.Template,
            Message: renderedMessage.Message,
            Properties: renderedMessage.Properties,
            Context: frozenContext,
            EventId: eventId,
            // TenantId stamped from the ambient HeraldTenantScope on the
            // producer thread. Frozen onto the event so downstream sinks
            // (and the drain-entry assertion in FastPathAsyncSink) can
            // verify the event's audit context without reading AsyncLocal
            // on the wrong thread.
            TenantId: HeraldTenantScope.Current);

        // Return pooled collections after extracting all needed data
        CollectionPool.ReturnPropertyList(pooledProps);
        CollectionPool.ReturnContextDictionary(pooledCtx);

        return logEvent;
    }

    /// <summary>
    /// Fast path for events with no enrichers, no scope, no caller context.
    /// Skips pooled collection rental and enrichment — just parse template and create event.
    /// The <paramref name="defaultContext"/> is used directly as the event's Context
    /// without a copy, which is safe because Herald treats that value as immutable
    /// (it comes from the StructuredLogger's <c>_defaultContext</c> field and is only
    /// replaced via <c>ForContext</c>, never mutated in place).
    /// Saves ~100-200ns per event on pipelines with NullLogEnricher.
    /// </summary>
    private LogEvent CreateFastPath(
        LogLevel level, LogCategory category, string messageTemplate,
        IReadOnlyList<LogProperty>? properties,
        IReadOnlyDictionary<string, object?>? defaultContext,
        LogEventId? eventId)
    {
        var effectiveProps = properties ?? LogEvent.EmptyProperties;

        RenderedMessage renderedMessage;
        try
        {
            renderedMessage = _messageTemplateParser.Render(messageTemplate, effectiveProps);
        }
        catch (Exception ex)
        {
            renderedMessage = new RenderedMessage(
                Template: messageTemplate,
                Message: FallbackText.TemplateError(ex, messageTemplate),
                Properties: effectiveProps);
        }

        return new LogEvent(
            TimeUtc: _dateTimeProvider.GetUtcNow(),
            Level: level,
            Category: category,
            MessageTemplate: renderedMessage.Template,
            Message: renderedMessage.Message,
            Properties: renderedMessage.Properties,
            Context: defaultContext ?? LogEvent.EmptyContext,
            EventId: eventId,
            TenantId: HeraldTenantScope.Current);
    }

    // Rough total of stringified property-value lengths. Uses ToString() on
    // ResolvedValue — cheap, allocates once per property. Skips properties
    // with null values. The cap is per-event, not per-property, so one
    // 300 KiB value wins and one event with 300 one-KiB values also wins.
    //
    // No try/catch around ResolvedValue: that getter already catches
    // lazy-factory throws internally and returns a fallback string
    // (FallbackText), so it never propagates. A redundant try block here taxed
    // the JIT's loop-body analysis on the common (non-lazy) path for zero
    // benefit on the lazy path.
    private static (long Total, ToStringFailures? Failures) ComputePropertyByteCount(IReadOnlyList<LogProperty>? properties)
    {
        if (properties is null or { Count: 0 }) return (0, null);

        long total = 0;
        ToStringFailures? failures = null; // allocated only when a ToString throws
        for (var i = 0; i < properties.Count; i++)
        {
            var value = properties[i].ResolvedValue;
            if (value is null) continue;
            // Character count is a conservative proxy for byte count on
            // UTF-16 strings and close enough for numeric/bool ToString()
            // results. If a property's value is a huge byte[] we want the
            // cap to trip, so fall through to the ToString length rather
            // than inspecting the array.
            if (value is string text) total += text.Length;
            else if (failures is not null && failures.ByObject.TryGetValue(value, out var known)) failures.ByIndex.Add((i, known));
            else if (TextLength(value) is var (length, error) && error is null) total += length;
            else (failures ??= new ToStringFailures()).Add(i, value, error!);
            if (total > int.MaxValue) return (int.MaxValue, failures); // saturate — caller only compares against a cap
        }
        return (total, failures);
    }

    // The first ToString failure of each value: by property index, and by object identity, so a second property
    // that references the same object is replaced too, whether it comes before or after the failing one.
    private sealed class ToStringFailures
    {
        public List<(int Index, Exception Error)> ByIndex { get; } = [];
        public Dictionary<object, Exception> ByObject { get; } = new(ReferenceEqualityComparer.Instance);

        public void Add(int index, object value, Exception error)
        {
            ByIndex.Add((index, error));
            ByObject.TryAdd(value, error);
        }
    }

    // Rare path: copies the list and replaces each failed value with fallback text. It never calls ToString:
    // the size check's failure is final. A non-lazy property is matched by identity; a lazy one by index,
    // so its factory is not run again.
    private static IReadOnlyList<LogProperty> WithFailedValuesReplaced(IReadOnlyList<LogProperty> properties, ToStringFailures failures)
    {
        var errors = new Exception?[properties.Count];
        foreach (var (index, error) in failures.ByIndex) errors[index] = error;

        var copy = new LogProperty[properties.Count];
        for (var i = 0; i < properties.Count; i++)
        {
            var p = properties[i];
            var error = errors[i]
                ?? (p.Value is { } raw and not string and not Func<object?> && failures.ByObject.TryGetValue(raw, out var shared) ? shared : null);
            copy[i] = error is null
                ? p
                : new LogProperty(p.Name, FallbackText.ValueToStringThrew(p.Name, error), p.CaptureMode, p.Format, p.Visibility);
        }
        return copy;
    }

    // A value's ToString can throw (a broken override, a hostile type, one that fails only sometimes).
    // Kept out of the loop above so the common string path stays free of a try block.
    private static (int Length, Exception? Error) TextLength(object value)
    {
        try
        {
            return (value.ToString()?.Length ?? 0, null);
        }
        catch (Exception ex)
        {
            return (0, ex);
        }
    }

    private static void MergeContextInto(
        Dictionary<string, object?> destination,
        IReadOnlyDictionary<string, object?>? source) {
        if (source is null or { Count: 0 })
        {
            return;
        }

        foreach (var pair in source)
        {
            destination[pair.Key] = pair.Value;
        }
    }

    /// <summary>
    /// Freeze the mutable enrichment context into an immutable dictionary for the LogEvent.
    ///
    /// Pooling optimization: the pooled dictionary will be returned after this call,
    /// so we must copy its contents into a new dictionary. We use the count as a
    /// capacity hint to avoid internal resizing (one allocation, right-sized).
    /// When the context is empty, we return the shared EmptyContext singleton (zero allocation).
    /// </summary>
    private static IReadOnlyDictionary<string, object?> CreateFrozenContext(
        LogEventEnrichmentContext enrichmentContext)
    {
        if (enrichmentContext.Context.Count == 0)
            return LogEvent.EmptyContext;

        return new Dictionary<string, object?>(
            enrichmentContext.Context, StringComparer.Ordinal);
    }
}
