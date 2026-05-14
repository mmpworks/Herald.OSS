#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Templating;
using MMP.Herald.Time;

namespace MMP.Herald.Spans;

/// <summary>
/// Creates structured logging spans with automatic lifecycle tracking.
///
/// Usage:
///   var spanFactory = new LogSpanFactory(logger, timeProvider);
///
///   using var span = spanFactory.Begin("LoadLevel",
///       new LogProperty("levelName", "DarkwoodHollow"));
///   // ... work happens, child events carry span context ...
///   // On dispose: exit event with duration is emitted automatically.
/// </summary>
public sealed class LogSpanFactory
{
    private readonly StructuredLogger _logger;
    private readonly IDateTimeProvider _timeProvider;
    private readonly ISpanMetricsCollector? _metricsCollector;
    private static long _spanCounter;

    public LogSpanFactory(
        StructuredLogger logger,
        IDateTimeProvider timeProvider,
        ISpanMetricsCollector? metricsCollector = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _metricsCollector = metricsCollector;
    }

    /// <summary>
    /// Begin a new span. If a span is active on the current async flow
    /// (via SpanContext), the new span becomes its child automatically.
    /// Otherwise it starts as a root span.
    /// </summary>
    public LogSpan Begin(string spanName, params LogProperty[] properties)
    {
        var parent = SpanContext.Current;
        return BeginInternal(_logger, _timeProvider, _metricsCollector, spanName,
            parentSpanId: parent?.SpanId, rootSpanId: parent?.RootSpanId, properties);
    }

    internal static LogSpan BeginInternal(
        StructuredLogger logger,
        IDateTimeProvider timeProvider,
        ISpanMetricsCollector? metricsCollector,
        string spanName,
        string? parentSpanId,
        string? rootSpanId,
        LogProperty[] properties)
    {
        var spanId = GenerateSpanId();
        var startTime = timeProvider.GetUtcNow();

        // Root spans set themselves as the root. Child spans inherit the root.
        var effectiveRootSpanId = rootSpanId ?? spanId;

        var scopeValues = new Dictionary<string, object?>
        {
            ["spanId"] = spanId,
            ["spanName"] = spanName,
            ["rootSpanId"] = effectiveRootSpanId
        };

        if (parentSpanId is not null)
        {
            scopeValues["parentSpanId"] = parentSpanId;
        }

        // Push scope so all events within this span carry the context
        var scope = logger.BeginScope(scopeValues);

        // Emit the enter lifecycle event.
        // The spanEvent context key lets downstream consumers efficiently filter
        // span lifecycle events without parsing the message template.
        var enterProperties = new List<LogProperty>
        {
            new("spanName", spanName),
            new("spanId", spanId)
        };

        if (parentSpanId is not null)
        {
            enterProperties.Add(new LogProperty("parentSpanId", parentSpanId));
        }

        if (properties.Length > 0)
        {
            enterProperties.AddRange(properties);
        }

        logger.Debug(LogCategory.App, "Span {spanName} entered",
            context: new Dictionary<string, object?> { ["spanEvent"] = "enter" },
            properties: enterProperties);

        return new LogSpan(logger, scope, timeProvider, metricsCollector,
            spanName, spanId, parentSpanId, effectiveRootSpanId, startTime);
    }

    private static string GenerateSpanId()
    {
        var counter = Interlocked.Increment(ref _spanCounter);
        return $"span-{counter:x8}";
    }
}
