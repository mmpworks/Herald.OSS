#nullable enable

using System;
using System.Threading;
using MMP.Herald.Events;
using MMP.Herald.Predicates;

namespace MMP.Herald.Filters;

/// <summary>
/// Time-window rate limiter. Allows at most N events per window duration.
/// Events matching the optional scope predicate are throttled; all others pass through.
/// Uses Interlocked operations for lock-free thread safety.
/// </summary>
public sealed class ThrottlingFilter : ILogFilter
{
    private readonly int _maxEventsPerWindow;
    private readonly long _windowDurationTicks;
    private readonly ILogPredicate? _scope;
    private long _windowStartTicks;
    private long _windowCount;

    public ThrottlingFilter(
        int maxEventsPerWindow,
        TimeSpan windowDuration,
        ILogPredicate? scope = null)
    {
        if (maxEventsPerWindow <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxEventsPerWindow), maxEventsPerWindow,
                "Max events per window must be greater than zero.");
        }

        if (windowDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(windowDuration), windowDuration,
                "Window duration must be greater than zero.");
        }

        _maxEventsPerWindow = maxEventsPerWindow;
        _windowDurationTicks = windowDuration.Ticks;
        _scope = scope;
        _windowStartTicks = Environment.TickCount64;
    }

    // -- Inspection --
    public int MaxEventsPerWindow => _maxEventsPerWindow;
    public TimeSpan WindowDuration => TimeSpan.FromTicks(_windowDurationTicks);

    public bool Allow(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        if (_scope is not null && !_scope.Evaluate(logEvent))
        {
            return true;
        }

        var now = Environment.TickCount64;
        var windowStart = Interlocked.Read(ref _windowStartTicks);

        // Window expired — attempt atomic reset.
        // Cognitive complexity note: the CompareExchange handles the race where multiple threads
        // detect expiration simultaneously. Only the winner resets; losers re-read and proceed.
        if (now - windowStart >= _windowDurationTicks / TimeSpan.TicksPerMillisecond)
        {
            if (Interlocked.CompareExchange(ref _windowStartTicks, now, windowStart) == windowStart)
            {
                Interlocked.Exchange(ref _windowCount, 0);
            }
        }

        var count = Interlocked.Increment(ref _windowCount);
        return count <= _maxEventsPerWindow;
    }
}
