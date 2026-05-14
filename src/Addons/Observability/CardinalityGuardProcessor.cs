#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;
using MMP.Herald.Templating;

namespace MMP.Herald.Addons.Observability;

/// <summary>
/// Event processor that guards against unbounded cardinality in property values.
/// High-cardinality properties (e.g., userId, requestId, entityId) can explode
/// metric storage when used as labels/dimensions in downstream systems like
/// Prometheus, Grafana, or Datadog.
///
/// This processor monitors the distinct value count for configured property names.
/// When a property exceeds its cardinality limit, the value is replaced with
/// a sentinel string (default: "__high_cardinality__") so downstream metric
/// systems aggregate instead of exploding.
///
/// The original value is preserved as a silent property (e.g., "_raw.userId")
/// so JSON sinks and log storage still have the real value for forensic queries.
///
/// Usage:
///   var guard = new CardinalityGuardProcessor(
///       CardinalityRule.For("userId", maxDistinct: 1000),
///       CardinalityRule.For("entityId", maxDistinct: 500),
///       CardinalityRule.For("sessionId", maxDistinct: 2000));
///
///   builder.WithEventProcessor("cardinalityGuard", guard);
///
/// When "userId" reaches 1001 distinct values, subsequent new values are
/// replaced with "__high_cardinality__" in the visible property. The real
/// value is preserved as LogProperty.Silent("_raw.userId", originalValue).
/// </summary>
public sealed class CardinalityGuardProcessor : ILogEventProcessor
{
    private readonly Dictionary<string, CardinalityRule> _rules;
    private readonly ConcurrentDictionary<string, CardinalityTracker> _trackers = new();
    private long _breachCount;

    public CardinalityGuardProcessor(params CardinalityRule[] rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = new Dictionary<string, CardinalityRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
            _rules[rule.PropertyName] = rule;
    }

    public LogEvent? Process(LogEvent logEvent)
    {
        if (logEvent.Properties.Count == 0 || _rules.Count == 0)
            return logEvent;

        List<int>? indicesToReplace = null;

        for (var i = 0; i < logEvent.Properties.Count; i++)
        {
            var prop = logEvent.Properties[i];
            if (!_rules.TryGetValue(prop.Name, out var rule))
                continue;

            var tracker = _trackers.GetOrAdd(prop.Name,
                _ => new CardinalityTracker(rule.MaxDistinctValues));

            var valueStr = prop.ResolvedValue?.ToString() ?? "";

            if (!tracker.TryAdd(valueStr))
            {
                // Cardinality limit exceeded for this property
                indicesToReplace ??= new List<int>();
                indicesToReplace.Add(i);
            }
        }

        if (indicesToReplace is null)
            return logEvent;

        // Build new property array with guarded values
        Interlocked.Add(ref _breachCount, indicesToReplace.Count);

        var newProps = new LogProperty[logEvent.Properties.Count + indicesToReplace.Count];
        var extraIndex = logEvent.Properties.Count;

        for (var i = 0; i < logEvent.Properties.Count; i++)
        {
            var prop = logEvent.Properties[i];

            if (indicesToReplace.Contains(i))
            {
                var rule = _rules[prop.Name];
                // Preserve original as silent property
                newProps[extraIndex++] = LogProperty.Silent($"_raw.{prop.Name}", prop.ResolvedValue);
                // Replace visible property with sentinel
                newProps[i] = new LogProperty(prop.Name, rule.Sentinel,
                    prop.CaptureMode, prop.Format, prop.Visibility);
            }
            else
            {
                newProps[i] = prop;
            }
        }

        return logEvent with { Properties = newProps };
    }

    /// <summary>Total number of cardinality breaches detected.</summary>
    public long BreachCount => Interlocked.Read(ref _breachCount);

    /// <summary>Current distinct value count for a tracked property.</summary>
    public int GetDistinctCount(string propertyName) =>
        _trackers.TryGetValue(propertyName, out var tracker) ? tracker.Count : 0;

    /// <summary>Check if a property has exceeded its cardinality limit.</summary>
    public bool IsBreached(string propertyName) =>
        _trackers.TryGetValue(propertyName, out var tracker) && tracker.IsAtLimit;

    private sealed class CardinalityTracker
    {
        private readonly int _maxDistinct;
        private readonly ConcurrentDictionary<string, byte> _seen = new(StringComparer.Ordinal);

        public CardinalityTracker(int maxDistinct) => _maxDistinct = maxDistinct;

        /// <summary>
        /// Try to add a value. Returns true if the value is within cardinality limits
        /// (either already seen or new and under the limit). Returns false if the value
        /// is new and would exceed the limit.
        /// </summary>
        public bool TryAdd(string value)
        {
            if (_seen.ContainsKey(value))
                return true;

            if (_seen.Count >= _maxDistinct)
                return false;

            _seen.TryAdd(value, 0);
            return true;
        }

        public int Count => _seen.Count;
        public bool IsAtLimit => _seen.Count >= _maxDistinct;
    }
}

/// <summary>
/// Rule defining the cardinality limit for a specific property name.
/// </summary>
public sealed record CardinalityRule(
    string PropertyName,
    int MaxDistinctValues,
    string Sentinel = "__high_cardinality__")
{
    /// <summary>Convenience factory.</summary>
    public static CardinalityRule For(string propertyName, int maxDistinct,
        string sentinel = "__high_cardinality__") =>
        new(propertyName, maxDistinct, sentinel);
}
