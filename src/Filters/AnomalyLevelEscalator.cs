#nullable enable

using System;
using System.Threading;
using MMP.Herald.Events;
using MMP.Herald.Levels;

namespace MMP.Herald.Filters;

/// <summary>
/// Pipeline decorator that monitors error rates and automatically
/// escalates the log level when anomalies are detected.
///
/// When the error count in a sliding window exceeds the threshold,
/// the escalator lowers the LogLevelSwitch to the escalated level
/// (e.g., debug) so detailed events start flowing. When the window
/// clears, it restores the original level.
///
/// No framework in any language has this as a first-class feature.
///
/// Usage:
///   var escalator = new AnomalyLevelEscalator(
///       inner: pipeline,
///       levelRegistry: registry,
///       levelSwitch: globalSwitch,
///       errorLevel: KnownLogLevels.Error,
///       escalatedLevel: KnownLogLevels.Debug,
///       windowSeconds: 60,
///       errorThreshold: 10);
/// </summary>
public sealed class AnomalyLevelEscalator : ILogger, MMP.Herald.Pipeline.IComponentMetadata
{
    private readonly ILogger _inner;
    private readonly ILogLevelRegistry _levelRegistry;
    private readonly LogLevelSwitch _levelSwitch;
    private readonly LogLevel _errorLevel;
    private readonly LogLevel _escalatedLevel;
    private readonly LogLevel _normalLevel;
    private readonly int _windowSeconds;
    private readonly int _errorThreshold;

    // Sliding window: circular buffer of timestamps
    private readonly long[] _errorTimestamps;
    private int _writeIndex;
    private int _errorCount;
    private readonly object _sync = new();
    private volatile bool _isEscalated;

    public AnomalyLevelEscalator(
        ILogger inner,
        ILogLevelRegistry levelRegistry,
        LogLevelSwitch levelSwitch,
        LogLevel errorLevel,
        LogLevel escalatedLevel,
        int windowSeconds = 60,
        int errorThreshold = 10)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _levelRegistry = levelRegistry ?? throw new ArgumentNullException(nameof(levelRegistry));
        _levelSwitch = levelSwitch ?? throw new ArgumentNullException(nameof(levelSwitch));
        _errorLevel = errorLevel ?? throw new ArgumentNullException(nameof(errorLevel));
        _escalatedLevel = escalatedLevel ?? throw new ArgumentNullException(nameof(escalatedLevel));
        _normalLevel = levelSwitch.MinimumLevel;
        _windowSeconds = windowSeconds;
        _errorThreshold = errorThreshold;
        _errorTimestamps = new long[errorThreshold * 2]; // buffer larger than threshold
    }

    /// <summary>
    /// Whether the escalator is currently in escalated (high-detail) mode.
    /// </summary>
    public bool IsEscalated => _isEscalated;

    public void Log(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        if (_levelRegistry.IsAtOrAbove(logEvent.Level, _errorLevel))
        {
            RecordError();
        }

        _inner.Log(logEvent);
    }

    private void RecordError()
    {
        var now = Environment.TickCount64;
        var windowMs = _windowSeconds * 1000L;

        lock (_sync)
        {
            // Record this error
            _errorTimestamps[_writeIndex] = now;
            _writeIndex = (_writeIndex + 1) % _errorTimestamps.Length;
            _errorCount = Math.Min(_errorCount + 1, _errorTimestamps.Length);

            // Count errors within the window
            var recentErrors = 0;
            for (var i = 0; i < _errorCount; i++)
            {
                if (now - _errorTimestamps[i] <= windowMs)
                {
                    recentErrors++;
                }
            }

            if (recentErrors >= _errorThreshold && !_isEscalated)
            {
                _levelSwitch.MinimumLevel = _escalatedLevel;
                _isEscalated = true;
            }
            else if (recentErrors < _errorThreshold && _isEscalated)
            {
                _levelSwitch.MinimumLevel = _normalLevel;
                _isEscalated = false;
            }
        }
    }

    // -- IComponentMetadata --
    string Pipeline.IComponentMetadata.ComponentName => "anomalyEscalator";
    string Pipeline.IComponentMetadata.DisplayName => "Anomaly Level Escalator";
    string Pipeline.IComponentMetadata.Description => "Auto-lowers minimum level when error rates spike.";
    string Pipeline.IComponentMetadata.Help => "Monitors error rate within a sliding window. When errors exceed threshold, temporarily drops to debug level for full context. Reverts when rate normalizes.";
    Pipeline.VendorInfo Pipeline.IComponentMetadata.Vendor => Pipeline.VendorInfo.MMP;
    System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> Pipeline.IComponentMetadata.ConfigurationSchema => [];
}
