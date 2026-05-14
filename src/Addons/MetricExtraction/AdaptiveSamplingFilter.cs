#nullable enable

using System;
using System.Threading;
using MMP.Herald.Events;
using MMP.Herald.Filters;
using MMP.Herald.Levels;

namespace MMP.Herald.Addons.MetricExtraction;

/// <summary>
/// Adaptive sampling that automatically adjusts retention rate based on
/// real-time error conditions. During quiet periods, samples aggressively
/// (keep fewer events). During error spikes, captures everything.
///
/// Pairs naturally with AnomalyLevelEscalator but operates at the
/// sampling layer rather than the level layer.
///
/// Algorithm:
/// - Tracks error count in a sliding window
/// - When errors exceed threshold, sampling rate drops to 1 (keep all)
/// - When errors are below threshold, sampling rate rises to configured max
/// - Transition is immediate (not gradual) for simplicity
///
/// Error events (level >= errorLevel) always pass through regardless of sampling.
/// </summary>
public sealed class AdaptiveSamplingFilter : ILogFilter
{
    private readonly int _normalSampleRate;
    private readonly int _errorThreshold;
    private readonly long _windowTicks;
    private readonly ILogLevelRegistry _levelRegistry;
    private readonly LogLevel _errorLevel;

    private long _windowStartTicks;
    private int _errorCount;
    private long _eventCounter;

    public AdaptiveSamplingFilter(
        int normalSampleRate,
        int errorThreshold,
        TimeSpan window,
        ILogLevelRegistry levelRegistry,
        LogLevel? errorLevel = null) {
        if (normalSampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(normalSampleRate), "Must be positive.");
        }

        if (errorThreshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(errorThreshold), "Must be positive.");
        }

        _normalSampleRate = normalSampleRate;
        _errorThreshold = errorThreshold;
        _windowTicks = window.Ticks;
        _levelRegistry = levelRegistry ?? throw new ArgumentNullException(nameof(levelRegistry));
        _errorLevel = errorLevel ?? KnownLogLevels.Error;
        _windowStartTicks = DateTimeOffset.UtcNow.Ticks;
    }

    // -- Inspection --
    public int NormalSampleRate => _normalSampleRate;
    public int ErrorThreshold => _errorThreshold;

    /// <summary>
    /// Current effective sample rate. 1 = keep all, N = keep 1 in N.
    /// </summary>
    public int CurrentSampleRate =>
        Volatile.Read(ref _errorCount) >= _errorThreshold ? 1 : _normalSampleRate;

    /// <summary>
    /// Whether the filter is currently in "capture everything" mode.
    /// </summary>
    public bool IsEscalated => CurrentSampleRate == 1;

    public bool Allow(LogEvent logEvent) {
        var now = DateTimeOffset.UtcNow.Ticks;
        var windowStart = Volatile.Read(ref _windowStartTicks);

        // Check if window has expired
        if (now - windowStart >= _windowTicks)
        {
            // Reset window
            Interlocked.Exchange(ref _windowStartTicks, now);
            Interlocked.Exchange(ref _errorCount, 0);
        }

        // Count errors
        if (_levelRegistry.IsAtOrAbove(logEvent.Level, _errorLevel))
        {
            Interlocked.Increment(ref _errorCount);
            return true; // errors always pass
        }

        // Apply current sample rate
        var count = Interlocked.Increment(ref _eventCounter);
        var rate = CurrentSampleRate;

        return rate <= 1 || count % rate == 0;
    }
}
