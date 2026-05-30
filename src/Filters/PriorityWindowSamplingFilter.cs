#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using MMP.Herald.Events;
using MMP.Herald.Levels;

namespace MMP.Herald.Filters;

/// <summary>
/// Per-level priority sampler with a sliding-tumbling window guarantee.
///
/// <para>
/// Existing samplers (<see cref="SamplingFilter"/> 1-in-N,
/// <c>AdaptiveSamplingFilter</c>) are global: they decide admission without
/// caring which level the event carries. Under high-volume floods this hides
/// the events operators care about most. Pin "always keep at least 60 error
/// events per minute" with <see cref="PriorityWindowSamplingFilter"/>:
/// </para>
///
/// <code>
/// var sampler = new PriorityWindowSamplingFilter(
///     minimumPerLevel: new Dictionary&lt;LogLevel, int&gt;
///     {
///         [KnownLogLevels.Error] = 60,
///         [KnownLogLevels.Warning] = 200,
///     },
///     windowDuration: TimeSpan.FromMinutes(1),
///     defaultSampleRate: 1000);            // 1-in-1000 for everything else
///
/// builder.WithSamplingFilter(sampler);
/// </code>
///
/// <para>
/// Per call, the filter advances the per-level window if the system clock
/// crossed a boundary, then increments that level's counter. Events whose
/// counter is at or below the configured minimum admit unconditionally;
/// events past the minimum fall through to <c>defaultSampleRate</c>
/// (1-in-N) sampling. Levels not present in the minimum map use the default
/// rate from the first event.
/// </para>
///
/// <para><b>Thread safety.</b> Each level owns an isolated counter cell.
/// Window rollover and increment use <see cref="Interlocked.CompareExchange(ref long, long, long)"/>
/// against a packed (windowIndex, count) long, so concurrent callers across
/// every level pay no contention with each other and contention within a
/// single level reduces to a single CAS per event. No allocations on the hot
/// path; the cells dictionary is read-only after construction.</para>
///
/// <para><b>Edition.</b> Community.</para>
/// <para><b>Tests.</b> <c>tests/Filters/PriorityWindowSamplingFilterTests.cs</c>.</para>
/// </summary>
public sealed class PriorityWindowSamplingFilter : ILogFilter
{
    private readonly IReadOnlyDictionary<LogLevel, LevelCell> _cells;
    private readonly long _windowTicks;
    private readonly int? _defaultSampleRate;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>
    /// Build a priority sampler. <paramref name="minimumPerLevel"/> declares
    /// the per-level admit floor for each window; levels absent from the map
    /// use <paramref name="defaultSampleRate"/> directly.
    /// </summary>
    /// <param name="minimumPerLevel">Levels and the minimum number of events per window each is guaranteed to admit.</param>
    /// <param name="windowDuration">Window size. Each window is a tumbling slot — counters reset at the boundary.</param>
    /// <param name="defaultSampleRate">1-in-N sampling for events past the per-level minimum, and for levels not in the map. <c>null</c> admits everything past the minimum (effectively a floor only).</param>
    /// <param name="clock">Optional time source for deterministic tests.</param>
    public PriorityWindowSamplingFilter(
        IReadOnlyDictionary<LogLevel, int> minimumPerLevel,
        TimeSpan windowDuration,
        int? defaultSampleRate = null,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(minimumPerLevel);
        if (windowDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(windowDuration), windowDuration,
                "Window duration must be positive.");
        }
        if (defaultSampleRate is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultSampleRate), defaultSampleRate,
                "Default sample rate, when supplied, must be greater than zero.");
        }

        // Build the per-level cell map once. Allocating cells lazily on the
        // hot path would invite races on first observation; pre-creating
        // them keeps Allow allocation-free.
        var cells = new Dictionary<LogLevel, LevelCell>(minimumPerLevel.Count);
        foreach (var kv in minimumPerLevel)
        {
            if (kv.Key is null)
            {
                throw new ArgumentException("Minimum-per-level dictionary cannot contain null level keys.", nameof(minimumPerLevel));
            }
            if (kv.Value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumPerLevel),
                    $"Minimum for level '{kv.Key.Key}' must be non-negative; was {kv.Value}.");
            }
            cells[kv.Key] = new LevelCell(kv.Value);
        }

        _cells = cells;
        _windowTicks = windowDuration.Ticks;
        _defaultSampleRate = defaultSampleRate;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Total levels under quota.</summary>
    public int TrackedLevelCount => _cells.Count;

    /// <summary>Window duration.</summary>
    public TimeSpan WindowDuration => TimeSpan.FromTicks(_windowTicks);

    /// <summary>Default 1-in-N sampling rate applied past the per-level minimum.</summary>
    public int? DefaultSampleRate => _defaultSampleRate;

    public bool Allow(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var windowIndex = WindowIndex();

        if (_cells.TryGetValue(logEvent.Level, out var cell))
        {
            // Cell is present → check the per-level floor first.
            var positionInWindow = cell.AdvanceAndCount(windowIndex);
            if (positionInWindow <= cell.Minimum)
            {
                return true;
            }
            // Past the floor → fall through to default rate.
            return AdmitByDefaultRate(positionInWindow);
        }

        // Level not in the quota map → default rate only.
        // We share a counter with cells we do not track by reusing the
        // overflow position; a separate untracked counter would inflate
        // memory without changing semantics because the default rate is
        // memoryless modulo the counter.
        return AdmitByDefaultRate(NextUntrackedCount());
    }

    // -- Internal counter for untracked levels --

    private long _untrackedCount;
    private long NextUntrackedCount() => Interlocked.Increment(ref _untrackedCount);

    private bool AdmitByDefaultRate(long positionInWindow)
    {
        if (_defaultSampleRate is null)
        {
            // No fallback configured: anything past the floor is dropped.
            return false;
        }
        // 1-in-N modulo. Position 1 admits, 2..(N-1) drop, N admits, etc.
        return (ulong)positionInWindow % (ulong)_defaultSampleRate.Value == 0;
    }

    private long WindowIndex()
    {
        // Tumbling window: events are bucketed by floor(now / windowDuration).
        // Using ticks keeps the math integer-only and avoids floating-point
        // drift across long uptimes.
        var nowTicks = _clock().UtcTicks;
        return nowTicks / _windowTicks;
    }

    /// <summary>
    /// Per-level counter cell. State is packed into a single 64-bit value so
    /// window rollover and event count update happen atomically through one
    /// <see cref="Interlocked.CompareExchange(ref long, long, long)"/>.
    ///
    /// <para>
    /// Layout: low 32 bits hold the window index (mod 2^32, which is more
    /// than enough — at one window per second this overflows after 136
    /// years). High 32 bits hold the count within the window (capped at 2^31).
    /// </para>
    /// </summary>
    private sealed class LevelCell
    {
        public int Minimum { get; }
        private long _packed;

        public LevelCell(int minimum)
        {
            Minimum = minimum;
            _packed = 0;
        }

        public long AdvanceAndCount(long windowIndex)
        {
            var indexLow32 = unchecked((uint)windowIndex);

            while (true)
            {
                var current = Interlocked.Read(ref _packed);
                var currentWindow = unchecked((uint)current);
                var currentCount = (int)(current >> 32);

                long updated;
                long observedCount;
                if (currentWindow == indexLow32)
                {
                    // Same window: bump the count.
                    var nextCount = currentCount == int.MaxValue ? int.MaxValue : currentCount + 1;
                    observedCount = nextCount;
                    updated = ((long)nextCount << 32) | indexLow32;
                }
                else
                {
                    // New window: reset the count to 1 (this admit) and
                    // remember the new window index.
                    observedCount = 1;
                    updated = (1L << 32) | indexLow32;
                }

                if (Interlocked.CompareExchange(ref _packed, updated, current) == current)
                {
                    return observedCount;
                }
                // CAS lost — another thread updated. Loop and try again.
                // The retry is bounded by contention on this single level
                // and is short because the only state being raced is the
                // packed long.
            }
        }
    }
}
