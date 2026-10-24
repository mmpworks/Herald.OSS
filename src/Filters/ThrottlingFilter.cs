#nullable enable

using System;
using System.Threading;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Predicates;

namespace MMP.Herald.Filters;

/// <summary>
/// Caps how many events can pass in a fixed stretch of time. It lets through at most
/// N events per window, then drops the rest until the window ends.
/// </summary>
/// <remarks>
/// <para>
/// <b>What "window duration" means.</b> The window is a fixed block of time. It is not a
/// sliding window. The first window starts when the filter is built. When a window runs out,
/// the next event that arrives starts a new window and the count goes back to zero.
/// </para>
/// <para>
/// Example: 100 events per 1 second. Events 1 to 100 in a window pass. Event 101 and later
/// are dropped. When the second ends, the next event opens a fresh window and the count restarts.
/// </para>
/// <para>
/// <b>Edge effect.</b> Because windows are fixed, a burst that straddles a boundary can pass up to
/// 2N events in a span shorter than one window: N at the end of one window and N at the start of
/// the next. If you need a hard cap over any span, this filter is too coarse.
/// </para>
/// <para>
/// <b>Scope.</b> If you give a scope predicate, only events that match it are counted and
/// throttled. Every other event passes through and never touches the count.
/// </para>
/// <para>
/// <b>Thread safety.</b> No locks. The shared state is two numbers, changed with
/// <see cref="Interlocked"/> calls, so many threads can log at once.
/// </para>
/// </remarks>
public sealed class ThrottlingFilter : ILogFilter
{
    // The cap: how many events one window may let through.
    private readonly int _maxEventsPerWindow;

    // Window length stored in .NET ticks (100 ns each), because that is what TimeSpan gives us.
    // Allow() converts it to milliseconds before comparing against the clock.
    private readonly long _windowDurationTicks;

    // Optional gate. When set, only events this predicate accepts are counted.
    // Null means every event is counted.
    private readonly ILogPredicate? _scope;

    // Ranks event levels against _errorLevel. Set once in the constructor.
    private readonly ILogLevelRegistry _levelRegistry;

    // The lowest level that counts as an error. Set once in the constructor.
    private readonly LogLevel _errorLevel;

    // True means errors are counted and dropped like every other event.
    // False (the default) means errors skip the limiter.
    private readonly bool _throttleErrors;

    // When the current window began, read from Environment.TickCount64.
    // Despite the "Ticks" in the name, this holds MILLISECONDS (the clock's unit),
    // not .NET ticks. It is a monotonic clock, so a system clock change cannot break the window.
    private long _windowStartTicks;

    // How many in-scope events the current window has seen so far.
    // Reset to zero when a new window starts.
    private long _windowCount;

    /// <param name="maxEventsPerWindow">Most events allowed through per window. Must be above zero.</param>
    /// <param name="windowDuration">Length of one window. Must be above zero.</param>
    /// <param name="scope">Optional. Only events this predicate matches are throttled.</param>
    /// <remarks>Ranks levels with the default Herald level order.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">Either number is zero or negative.</exception>
    public ThrottlingFilter(
        int maxEventsPerWindow,
        TimeSpan windowDuration,
        ILogPredicate? scope = null)
        : this(maxEventsPerWindow, windowDuration, scope, new DefaultLogLevelRegistryFactory().Create())
    {
    }

    /// <param name="maxEventsPerWindow">Most events allowed through per window. Must be above zero.</param>
    /// <param name="windowDuration">Length of one window. Must be above zero.</param>
    /// <param name="scope">Optional. Only events this predicate matches are throttled.</param>
    /// <param name="levelRegistry">
    /// The level order used to decide which events are errors. Every event level must be registered in it.
    /// </param>
    /// <param name="throttleErrors">
    /// False (the default): events at <paramref name="errorLevel"/> and above skip the limiter.
    /// True: errors are counted and dropped like every other event.
    /// </param>
    /// <param name="errorLevel">The lowest level that counts as an error. Defaults to Error.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either number is zero or negative.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="levelRegistry"/> is null.</exception>
    public ThrottlingFilter(
        int maxEventsPerWindow,
        TimeSpan windowDuration,
        ILogPredicate? scope,
        ILogLevelRegistry levelRegistry,
        bool throttleErrors = false,
        LogLevel? errorLevel = null)
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
        _levelRegistry = levelRegistry ?? throw new ArgumentNullException(nameof(levelRegistry));
        _errorLevel = errorLevel ?? KnownLogLevels.Error;
        _throttleErrors = throttleErrors;
        // The first window starts now, at construction, not at the first event.
        _windowStartTicks = Environment.TickCount64;
    }

    // -- Inspection --
    // Read-only views of the settings, for tests and diagnostics.

    /// <summary>The cap this filter was built with.</summary>
    public int MaxEventsPerWindow => _maxEventsPerWindow;

    /// <summary>The window length this filter was built with.</summary>
    public TimeSpan WindowDuration => TimeSpan.FromTicks(_windowDurationTicks);

    /// <summary>True when errors are counted and dropped like every other event.</summary>
    public bool ThrottlesErrors => _throttleErrors;

    /// <summary>
    /// Decides whether one event may continue down the pipeline.
    /// Returns true to keep the event, false to drop it.
    /// </summary>
    public bool Allow(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        // Step 1: out-of-scope events skip the limiter. They are never counted and never dropped.
        if (_scope is not null && !_scope.Evaluate(logEvent))
        {
            return true;
        }

        // Step 1b: errors skip the limiter and are not counted, so a burst of errors
        // cannot use up the budget that lower levels share.
        if (IsSparedError(logEvent))
        {
            return true;
        }

        // Step 2: read the clock and the start of the current window (both in milliseconds).
        var now = Environment.TickCount64;
        var windowStart = Interlocked.Read(ref _windowStartTicks);

        // Step 3: has the window run out? The stored duration is in .NET ticks,
        // so divide down to milliseconds to match the clock.
        // Cognitive complexity note: several threads can notice the expiry at the same moment.
        // CompareExchange lets exactly one of them move the start time forward.
        // The winner also zeroes the count. The losers skip the reset and carry on
        // with the new window the winner just opened.
        // Edge case: at the instant of reset, one event may be counted in the old window
        // or lost from the new one. That is a small error, accepted to keep the path lock-free.
        if (now - windowStart >= _windowDurationTicks / TimeSpan.TicksPerMillisecond)
        {
            if (Interlocked.CompareExchange(ref _windowStartTicks, now, windowStart) == windowStart)
            {
                Interlocked.Exchange(ref _windowCount, 0);
            }
        }

        // Step 4: count this event. Increment returns the new total, so the first event
        // in a window gets 1. Events up to the cap pass; the cap + 1 event and later are dropped.
        var count = Interlocked.Increment(ref _windowCount);
        return count <= _maxEventsPerWindow;
    }

    // True when the event is an error and errors are not throttled.
    // IsAtOrAbove reads the registry's rank cache: no lock, no allocation.
    private bool IsSparedError(LogEvent logEvent) =>
        !_throttleErrors && _levelRegistry.IsAtOrAbove(logEvent.Level, _errorLevel);
}
