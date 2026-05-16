#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Enrichers;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Pooling;
using MMP.Herald.Templating;
using MMP.Herald.Time;

namespace MMP.Herald.Events;

/// <summary>
/// Event factory that defers message template rendering.
/// Produces a LogEvent with Message = "" and raw properties, marked with a
/// sentinel context value so RenderingLogger knows to render on the worker thread.
///
/// Use with AsyncLogger + RenderingLogger to move rendering off the calling thread.
///
/// Pooling: uses CollectionPool for the merge dictionary, then copies to a frozen
/// dictionary for the immutable LogEvent. The pooled dict is returned immediately.
/// </summary>
public sealed class DeferredLogEventFactory : ILogEventFactory
{
    /// <summary>
    /// Context key used to mark events as deferred (not yet rendered).
    /// RenderingLogger checks for this sentinel to know when to render.
    /// </summary>
    public const string DeferredSentinel = "_herald:deferred";

    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogScopeProvider _scopeProvider;
    private readonly ILogEnricher _enricher;

    public DeferredLogEventFactory(
        IDateTimeProvider dateTimeProvider,
        ILogScopeProvider scopeProvider,
        ILogEnricher enricher) {
        _dateTimeProvider = dateTimeProvider;
        _scopeProvider = scopeProvider;
        _enricher = enricher;
    }

    public LogEvent Create(
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        IReadOnlyList<LogProperty>? properties = null,
        IReadOnlyDictionary<string, object?>? defaultContext = null,
        IReadOnlyDictionary<string, object?>? context = null,
        LogEventId? eventId = null) {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(messageTemplate);

        // Rent pooled collections for merging, then freeze into immutable copies
        var pooledCtx = CollectionPool.RentContextDictionary();
        var pooledProps = CollectionPool.RentPropertyList();

        if (properties is not null)
        {
            for (var i = 0; i < properties.Count; i++)
                pooledProps.Add(properties[i]);
        }

        MergeInto(pooledCtx, defaultContext);
        MergeInto(pooledCtx, _scopeProvider.GetMergedContext());
        MergeInto(pooledCtx, context);

        var enrichmentContext = new LogEventEnrichmentContext(
            level, category, messageTemplate, pooledProps, pooledCtx, pooled: true);

        _enricher.Enrich(enrichmentContext);

        // Add deferred sentinel and freeze into an immutable dictionary
        pooledCtx[DeferredSentinel] = "true";
        var frozenContext = new Dictionary<string, object?>(pooledCtx, StringComparer.Ordinal);

        // Snapshot properties into a right-sized array before returning the pooled list.
        // The pooled list will be Clear()'d on return, so we must copy here.
        var frozenProperties = pooledProps.Count > 0
            ? (IReadOnlyList<LogProperty>)pooledProps.ToArray()
            : LogEvent.EmptyProperties;

        CollectionPool.ReturnPropertyList(pooledProps);
        CollectionPool.ReturnContextDictionary(pooledCtx);

        return new LogEvent(
            TimeUtc: _dateTimeProvider.GetUtcNow(),
            Level: level,
            Category: category,
            MessageTemplate: messageTemplate,
            Message: "",
            Properties: frozenProperties,
            Context: frozenContext,
            EventId: eventId);
    }

    private static void MergeInto(
        Dictionary<string, object?> destination,
        IReadOnlyDictionary<string, object?>? source) {
        if (source is null or { Count: 0 }) return;

        foreach (var pair in source)
            destination[pair.Key] = pair.Value;
    }
}
