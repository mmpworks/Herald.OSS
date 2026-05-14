#nullable enable

using System;
using System.Threading;
using MMP.Herald.Events;
using MMP.Herald.Predicates;

namespace MMP.Herald.Filters;

/// <summary>
/// Count-based "1 in N" sampling filter.
/// Events matching the optional scope predicate are sampled; all others pass through.
/// Uses Interlocked.Increment for lock-free thread safety in the game loop hot path.
/// </summary>
public sealed class SamplingFilter : ILogFilter
{
    private readonly int _sampleRate;
    private readonly ILogPredicate? _scope;
    private long _counter;

    public SamplingFilter(int sampleRate, ILogPredicate? scope = null)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate), sampleRate, "Sample rate must be greater than zero.");
        }

        _sampleRate = sampleRate;
        _scope = scope;
    }

    public bool Allow(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        if (_scope is not null && !_scope.Evaluate(logEvent))
        {
            return true;
        }

        // Use unsigned modulo to avoid sign issues if counter wraps past long.MaxValue.
        // In C#, negative % positive preserves the negative sign, which would break sampling.
        var count = (ulong)Interlocked.Increment(ref _counter);
        return count % (ulong)_sampleRate == 0;
    }

    // -- Inspection --

    /// <summary>The sampling rate (1 in N events are kept).</summary>
    public int SampleRate => _sampleRate;

    /// <summary>Total events evaluated by this filter.</summary>
    public long EventsEvaluated => Interlocked.Read(ref _counter);
}
