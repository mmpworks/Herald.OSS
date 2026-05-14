#nullable enable

using System;
using MMP.Herald.Events;

namespace MMP.Herald.Filters;

/// <summary>
/// Deterministic sampling filter that uses consistent hashing on a context key
/// (e.g., correlationId, traceId) to decide whether an event is sampled.
///
/// The same key value always produces the same decision, so all events sharing
/// a correlation ID are either all sampled or all dropped. This preserves
/// complete traces in distributed systems rather than producing fragments.
///
/// Usage:
///   // Sample 10% of traces, consistently by correlation ID:
///   var filter = new ConsistentSamplingFilter(
///       contextKey: LogContextKeys.CorrelationId,
///       samplePercentage: 10);
///
///   // All events with correlationId="req-abc" will be sampled or not, deterministically.
///   // Events without the context key always pass (no sampling applied).
/// </summary>
public sealed class ConsistentSamplingFilter : ILogFilter
{
    private readonly string _contextKey;
    private readonly uint _threshold;

    /// <summary>
    /// Create a consistent sampling filter.
    /// </summary>
    /// <param name="contextKey">Context key to hash (e.g., "correlationId", "traceId").</param>
    /// <param name="samplePercentage">Percentage of distinct key values to keep (1-100).</param>
    public ConsistentSamplingFilter(string contextKey, int samplePercentage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contextKey);

        if (samplePercentage is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(samplePercentage), samplePercentage,
                "Sample percentage must be between 1 and 100.");

        _contextKey = contextKey;
        // Map percentage to a threshold in uint range
        _threshold = (uint)(uint.MaxValue / 100.0 * samplePercentage);
    }

    public bool Allow(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        // Events without the context key always pass
        if (!logEvent.Context.TryGetValue(_contextKey, out var value) || value is null)
            return true;

        var hash = StableHash(value.ToString() ?? "");
        return hash <= _threshold;
    }

    /// <summary>
    /// FNV-1a 32-bit hash. Deterministic, fast, well-distributed.
    /// Not cryptographic -- used only for sampling distribution.
    /// </summary>
    private static uint StableHash(string input)
    {
        unchecked
        {
            const uint fnvOffset = 2166136261;
            const uint fnvPrime = 16777619;

            var hash = fnvOffset;
            foreach (var c in input)
            {
                hash ^= c;
                hash *= fnvPrime;
            }
            return hash;
        }
    }
}
