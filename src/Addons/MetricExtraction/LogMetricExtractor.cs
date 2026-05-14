#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;
using MMP.Herald.Templating;

namespace MMP.Herald.Addons.MetricExtraction;

/// <summary>
/// Extracts metrics (counters, gauges) from log events based on configurable rules.
/// Runs as an ILogEventProcessor in the event pipeline.
///
/// Rules match events by predicate and extract numeric values from properties.
///
/// Usage:
///   var extractor = new LogMetricExtractor([
///       new MetricRule("error_count", evt => evt.Level.Key == "error", MetricType.Counter),
///       new MetricRule("response_time", evt => true, MetricType.Gauge, propertyName: "duration")
///   ]);
///   builder.WithEventProcessor(extractor);
///
///   // Query metrics:
///   var errorCount = extractor.GetCounter("error_count");
///   var avgDuration = extractor.GetGaugeAverage("response_time");
/// </summary>
public sealed class LogMetricExtractor : ILogEventProcessor
{
    private readonly IReadOnlyList<MetricRule> _rules;
    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MetricAccumulator> _gauges = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public LogMetricExtractor(IReadOnlyList<MetricRule> rules) {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));

        foreach (var rule in rules)
        {
            if (rule.Type == MetricType.Counter)
            {
                _counters[rule.Name] = 0;
            }
            else
            {
                _gauges[rule.Name] = new MetricAccumulator();
            }
        }
    }

    public LogEvent? Process(LogEvent logEvent) {
        foreach (var rule in _rules)
        {
            if (!rule.Predicate(logEvent))
            {
                continue;
            }

            if (rule.Type == MetricType.Counter)
            {
                lock (_sync)
                {
                    _counters[rule.Name]++;
                }
            }
            else if (rule.PropertyName is not null)
            {
                // Uses LogEvent's built-in lazy property index (O(1) lookup)
                var value = ExtractNumericValue(logEvent, rule.PropertyName);
                if (value.HasValue)
                {
                    lock (_sync)
                    {
                        _gauges[rule.Name].Add(value.Value);
                    }
                }
            }
        }

        return logEvent;
    }

    public long GetCounter(string name) {
        lock (_sync) { return _counters.TryGetValue(name, out var v) ? v : 0; }
    }

    public double GetGaugeAverage(string name) {
        lock (_sync) { return _gauges.TryGetValue(name, out var acc) ? acc.Average : 0; }
    }

    public double GetGaugeSum(string name) {
        lock (_sync) { return _gauges.TryGetValue(name, out var acc) ? acc.Sum : 0; }
    }

    public long GetGaugeCount(string name) {
        lock (_sync) { return _gauges.TryGetValue(name, out var acc) ? acc.Count : 0; }
    }

    public MetricSnapshot GetSnapshot() {
        lock (_sync)
        {
            var counters = new Dictionary<string, long>(_counters);
            var gauges = new Dictionary<string, GaugeSnapshot>();

            foreach (var (name, acc) in _gauges)
            {
                gauges[name] = new GaugeSnapshot(acc.Count, acc.Sum, acc.Average);
            }

            return new MetricSnapshot(counters, gauges);
        }
    }

    private static double? ExtractNumericValue(LogEvent logEvent, string propertyName)
    {
        var property = logEvent.GetProperty(propertyName);
        if (property is null) return null;

        var resolved = property.Value.ResolvedValue;
        return resolved switch
        {
            int i => i,
            long l => l,
            float f => f,
            double d => d,
            decimal m => (double)m,
            _ => null
        };
    }

    private sealed class MetricAccumulator
    {
        public long Count;
        public double Sum;
        public double Average => Count > 0 ? Sum / Count : 0;

        public void Add(double value) {
            Count++;
            Sum += value;
        }
    }
}

public sealed record MetricRule(
    string Name,
    Func<LogEvent, bool> Predicate,
    MetricType Type,
    string? PropertyName = null);

public sealed record MetricType(string Value)
{
    public static MetricType Counter { get; } = new("Counter");
    public static MetricType Gauge { get; } = new("Gauge");
    public override string ToString() => Value;
}

public sealed record MetricSnapshot(
    IReadOnlyDictionary<string, long> Counters,
    IReadOnlyDictionary<string, GaugeSnapshot> Gauges);

public sealed record GaugeSnapshot(long Count, double Sum, double Average);
