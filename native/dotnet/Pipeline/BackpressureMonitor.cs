#nullable enable

using System;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Monitors an AsyncLogger's queue depth and reports backpressure state.
/// Game loops can check this to proactively reduce logging verbosity
/// before events start dropping.
///
/// Usage:
///   var monitor = new BackpressureMonitor(asyncLogger, capacity: 1024);
///
///   // In game loop:
///   if (monitor.State == BackpressureState.Critical)
///   {
///       // Temporarily raise minimum level to reduce volume
///       dynamicPolicy.GlobalLevelSwitch.MinimumLevel = KnownLogLevels.Warn;
///   }
///
///   // Or use the callback:
///   var monitor = new BackpressureMonitor(asyncLogger, capacity: 1024,
///       onStateChanged: (oldState, newState) =>
///       {
///           if (newState == BackpressureState.Critical)
///               logger.Warn(LogCategory.App, "Logging queue at {pct}%",
///                   properties: [new LogProperty("pct", monitor.UsagePercent)]);
///       });
/// </summary>
public sealed class BackpressureMonitor
{
    private readonly AsyncLogger _asyncLogger;
    private readonly int _capacity;
    private readonly int _warningThreshold;
    private readonly int _criticalThreshold;
    private readonly Action<BackpressureState, BackpressureState>? _onStateChanged;
    private int _lastState; // BackpressureState as int for Interlocked.CompareExchange

    /// <summary>
    /// Create a backpressure monitor.
    /// </summary>
    /// <param name="asyncLogger">The async logger to monitor.</param>
    /// <param name="capacity">Total queue capacity (must match AsyncLogger's capacity).</param>
    /// <param name="warningPercent">Queue usage percentage that triggers Warning state (default 70).</param>
    /// <param name="criticalPercent">Queue usage percentage that triggers Critical state (default 90).</param>
    /// <param name="onStateChanged">Optional callback when state transitions occur.</param>
    public BackpressureMonitor(
        AsyncLogger asyncLogger,
        int capacity,
        int warningPercent = 70,
        int criticalPercent = 90,
        Action<BackpressureState, BackpressureState>? onStateChanged = null)
    {
        _asyncLogger = asyncLogger ?? throw new ArgumentNullException(nameof(asyncLogger));
        _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
        _warningThreshold = capacity * warningPercent / 100;
        _criticalThreshold = capacity * criticalPercent / 100;
        _onStateChanged = onStateChanged;
    }

    /// <summary>Current queue depth.</summary>
    public int QueueDepth => _asyncLogger.QueueDepth;

    /// <summary>Current usage as a percentage (0-100).</summary>
    public int UsagePercent => _capacity > 0 ? _asyncLogger.QueueDepth * 100 / _capacity : 0;

    /// <summary>
    /// Check the current backpressure state. Call this from the game loop
    /// to decide whether to reduce logging verbosity.
    /// Invokes the onStateChanged callback on transitions.
    /// </summary>
    public BackpressureState State
    {
        get
        {
            var depth = _asyncLogger.QueueDepth;
            var newStateInt = depth >= _criticalThreshold ? (int)BackpressureState.Critical
                           : depth >= _warningThreshold ? (int)BackpressureState.Warning
                           : (int)BackpressureState.Normal;

            // CAS loop: only the thread that wins the transition fires the callback.
            // Read the current state atomically, then attempt to swap.
            var oldStateInt = System.Threading.Volatile.Read(ref _lastState);
            if (oldStateInt != newStateInt &&
                System.Threading.Interlocked.CompareExchange(ref _lastState, newStateInt, oldStateInt) == oldStateInt)
            {
                _onStateChanged?.Invoke((BackpressureState)oldStateInt, (BackpressureState)newStateInt);
            }

            return (BackpressureState)newStateInt;
        }
    }
}

/// <summary>Backpressure severity level.</summary>
public enum BackpressureState
{
    /// <summary>Queue usage is normal. No action needed.</summary>
    Normal,
    /// <summary>Queue is filling up. Consider reducing verbosity.</summary>
    Warning,
    /// <summary>Queue is nearly full. Events may start dropping.</summary>
    Critical
}
