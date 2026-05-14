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
/// A structured span with enter/exit lifecycle, automatic timing,
/// parent-child tracking, and context propagation.
///
/// Inspired by Rust's tracing crate: spans have a name, carry context,
/// and emit lifecycle events (enter/exit) with duration. Child events
/// logged within a span automatically inherit its context (spanId,
/// spanName, parentSpanId).
///
/// Usage:
///   using var span = spanFactory.Begin("ProcessCombatRound",
///       new LogProperty("round", 3));
///
///   // All events inside this block carry spanId + spanName context.
///   span.Event("Rolling initiative for {count} combatants",
///       new LogProperty("count", 5));
///
///   // Nested spans form parent-child relationships.
///   using var innerSpan = span.BeginChild("ResolveAttack");
///   innerSpan.Event("{attacker} attacks {target}",
///       new LogProperty("attacker", "Kaelen"),
///       new LogProperty("target", "Goblin"));
///
/// On dispose, each span emits an exit event with elapsed duration.
/// </summary>
public sealed class LogSpan : IDisposable
{
    private readonly StructuredLogger _logger;
    private readonly ILogScope _scope;
    private readonly IDateTimeProvider _timeProvider;
    private readonly ISpanMetricsCollector? _metricsCollector;
    private readonly string _spanName;
    private readonly string _spanId;
    private readonly string? _parentSpanId;
    private readonly string _rootSpanId;
    private readonly DateTimeOffset _startTime;
    private readonly LogSpan? _previousSpan;
    private bool _isDisposed;

    internal LogSpan(
        StructuredLogger logger,
        ILogScope scope,
        IDateTimeProvider timeProvider,
        ISpanMetricsCollector? metricsCollector,
        string spanName,
        string spanId,
        string? parentSpanId,
        string rootSpanId,
        DateTimeOffset startTime)
    {
        _logger = logger;
        _scope = scope;
        _timeProvider = timeProvider;
        _metricsCollector = metricsCollector;
        _spanName = spanName;
        _spanId = spanId;
        _parentSpanId = parentSpanId;
        _rootSpanId = rootSpanId;
        _startTime = startTime;

        // Register this span as the current ambient span so child spans
        // created via spanFactory.Begin() auto-inherit parentage.
        _previousSpan = SpanContext.SetCurrent(this);
    }

    /// <summary>
    /// The unique identifier for this span.
    /// </summary>
    public string SpanId => _spanId;

    /// <summary>
    /// The human-readable name of this span.
    /// </summary>
    public string SpanName => _spanName;

    /// <summary>
    /// The parent span's identifier, or null for root spans.
    /// </summary>
    public string? ParentSpanId => _parentSpanId;

    /// <summary>
    /// Log an event within this span's context.
    /// The event automatically carries spanId and spanName.
    /// </summary>
    public void Event(string messageTemplate, params LogProperty[] properties)
    {
        _logger.Info(LogCategory.App, messageTemplate,
            properties: properties.Length > 0 ? properties : null);
    }

    /// <summary>
    /// Log a debug-level event within this span's context.
    /// </summary>
    public void Debug(string messageTemplate, params LogProperty[] properties)
    {
        _logger.Debug(LogCategory.App, messageTemplate,
            properties: properties.Length > 0 ? properties : null);
    }

    /// <summary>
    /// Log a warning within this span's context.
    /// </summary>
    public void Warn(string messageTemplate, params LogProperty[] properties)
    {
        _logger.Warn(LogCategory.App, messageTemplate,
            properties: properties.Length > 0 ? properties : null);
    }

    /// <summary>
    /// Begin a child span nested within this one.
    /// The child span inherits this span's context and tracks
    /// this span as its parent.
    /// </summary>
    /// <summary>
    /// The root span ID for this entire span tree.
    /// All spans in the tree share this value, enabling tree-wide correlation
    /// for external systems like retroactive enrichment.
    /// </summary>
    public string RootSpanId => _rootSpanId;

    public LogSpan BeginChild(string spanName, params LogProperty[] properties)
    {
        return LogSpanFactory.BeginInternal(
            _logger, _timeProvider, _metricsCollector, spanName,
            _spanId, _rootSpanId, properties);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        var elapsed = _timeProvider.GetUtcNow() - _startTime;

        _metricsCollector?.RecordSpanDuration(_spanName, elapsed, _parentSpanId);

        _logger.Info(LogCategory.App, "Span {spanName} completed in {elapsed}ms",
            context: new Dictionary<string, object?> { ["spanEvent"] = "exit" },
            properties:
            [
                new LogProperty("spanName", _spanName),
                new LogProperty("elapsed", elapsed.TotalMilliseconds, Format: "F1")
            ]);

        _scope.Dispose();

        // Restore the previous span as the ambient current
        SpanContext.SetCurrent(_previousSpan);
    }
}
