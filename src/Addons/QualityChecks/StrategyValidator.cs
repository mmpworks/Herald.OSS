#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;
using MMP.Herald.Routing;
using MMP.Herald.Templating;

namespace MMP.Herald.Addons.QualityChecks;

/// <summary>
/// Pipeline decorator that validates the active PipelineStrategy on every
/// Nth event (configurable) and flags anti-patterns as silent properties.
///
/// This is the runtime equivalent of a Roslyn compile-time analyzer:
/// it watches the live pipeline and reports issues as they're observed.
///
/// Detected anti-patterns:
/// - Rendering before Async (template rendering on caller thread)
/// - Filtering after Batching without FlightRecorder (wasted batch buffer)
/// - FlightRecorder before Filtering (recorder can't buffer filtered events)
/// - Duplicate pipeline steps
///
/// Usage:
///   builder.WithEventProcessor("strategyValidator",
///       new StrategyValidator(strategy, checkInterval: 10_000));
///
/// Every 10,000th event, the validator re-runs PipelineStrategy.Validate()
/// and attaches warnings as silent properties. The dashboard can query
/// for events with _strategyWarning to surface issues.
///
/// This is an IConfigurablePipelineDecorator so it can also be inserted
/// as a pipeline step and configured from the dashboard.
/// </summary>
public sealed class StrategyValidator : ILogEventProcessor, IConfigurablePipelineDecorator
{
    private PipelineStrategy _strategy;
    private int _checkInterval;
    private long _eventCount;
    private IReadOnlyList<string> _cachedWarnings;

    /// <param name="strategy">The strategy to validate.</param>
    /// <param name="checkInterval">Re-validate every N events (default: 10,000).</param>
    public StrategyValidator(PipelineStrategy strategy, int checkInterval = 10_000)
    {
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _checkInterval = checkInterval > 0 ? checkInterval : 10_000;
        _cachedWarnings = strategy.Validate();
    }

    // ── ILogEventProcessor ───────────────────────────────────────────

    public LogEvent? Process(LogEvent logEvent)
    {
        var count = System.Threading.Interlocked.Increment(ref _eventCount);

        // Only re-validate periodically
        if (count % _checkInterval != 0)
        {
            // Attach cached warnings if any
            return _cachedWarnings.Count > 0
                ? AttachWarnings(logEvent, _cachedWarnings)
                : logEvent;
        }

        // Re-validate
        _cachedWarnings = _strategy.Validate();

        return _cachedWarnings.Count > 0
            ? AttachWarnings(logEvent, _cachedWarnings)
            : logEvent;
    }

    /// <summary>Current strategy warnings (cached from last validation).</summary>
    public IReadOnlyList<string> CurrentWarnings => _cachedWarnings;

    /// <summary>Total events processed.</summary>
    public long EventsProcessed => System.Threading.Interlocked.Read(ref _eventCount);

    private static LogEvent AttachWarnings(LogEvent logEvent, IReadOnlyList<string> warnings)
    {
        var newProps = new LogProperty[logEvent.Properties.Count + 1];
        for (var i = 0; i < logEvent.Properties.Count; i++)
            newProps[i] = logEvent.Properties[i];
        newProps[^1] = LogProperty.Silent("_strategyWarning",
            string.Join("; ", warnings));
        return logEvent with { Properties = newProps };
    }

    // ── IConfigurablePipelineDecorator ────────────────────────────────

    public string StepName => "strategyValidator";
    public string DisplayName => "Strategy Validator";
    public string Description => "Validates pipeline strategy for anti-patterns and flags warnings on events.";

    public IReadOnlyList<SinkConfigField> ConfigurationSchema =>
    [
        SinkConfigField.Int("checkInterval", _checkInterval,
            "Re-validate every N events (higher = less overhead)"),
    ];

    public IReadOnlyDictionary<string, object?> GetConfiguration() => new Dictionary<string, object?>
    {
        ["checkInterval"] = _checkInterval,
        ["currentWarnings"] = _cachedWarnings.Count > 0 ? string.Join("; ", _cachedWarnings) : "none",
        ["eventsProcessed"] = System.Threading.Interlocked.Read(ref _eventCount)
    };

    public (bool Success, string? Error) ApplyConfiguration(IReadOnlyDictionary<string, object?> values)
    {
        if (values.TryGetValue("checkInterval", out var ci) && ci is int interval && interval > 0)
            _checkInterval = interval;
        return (true, null);
    }

    public ILogger CreateDecorator(ILogger inner, PipelineAccessor? pipelineAccessor)
    {
        // As a pipeline decorator, wrap inner with an EventProcessingLogger
        // containing this validator as the sole processor.
        var processingLogger = new EventProcessingLogger(inner, [this]);
        pipelineAccessor?.Register(this);
        return processingLogger;
    }

    /// <summary>Update the strategy to validate (e.g., after RebuildFrom).</summary>
    public void UpdateStrategy(PipelineStrategy strategy)
    {
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _cachedWarnings = strategy.Validate();
    }
}
