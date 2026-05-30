#nullable enable

using System;
using System.Threading;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;

namespace MMP.Herald.Addons.Observability;

/// <summary>
/// Lightweight SLO error budget monitor that runs as an event processor.
/// Tracks the ratio of error events to total events over a sliding window
/// and fires a callback when the error budget is exhausted.
///
/// SLO concepts:
///   - SLO target: e.g., 99.9% success rate (0.999)
///   - Error budget: 1 - target = 0.001 (0.1% of events can be errors)
///   - Burn rate: how fast the budget is being consumed
///     - Burn rate 1.0 = budget consumed exactly on pace
///     - Burn rate 2.0 = budget consumed 2x faster than sustainable
///     - Burn rate 10.0 = page now, budget exhausted in 1/10th the window
///
/// The monitor uses a simple rolling counter (not a true sliding window)
/// that resets at configurable intervals. This is intentionally lightweight —
/// for production SLO tracking, use a dedicated system (Grafana, Datadog).
/// This monitor complements the flight recorder by providing early warning
/// before the error rate triggers a cascade.
///
/// Usage:
///   var monitor = new ErrorBudgetMonitor(
///       sloTarget: 0.999,          // 99.9% success rate
///       windowSeconds: 300,        // 5-minute rolling window
///       onBudgetExhausted: report =>
///           logger.Warn(LogCategory.App, "SLO budget exhausted: {burnRate}x burn",
///               properties: [new LogProperty("burnRate", report.BurnRate)]),
///       onBudgetRecovered: report =>
///           logger.Info(LogCategory.App, "SLO budget recovered"));
///
///   builder.WithEventProcessor("sloBudget", monitor);
/// </summary>
public sealed class ErrorBudgetMonitor : ILogEventProcessor
{
    private readonly double _sloTarget;
    private readonly double _errorBudgetFraction;
    private readonly long _windowTicks;
    private readonly ILogLevelRegistry? _levelRegistry;
    private readonly LogLevel _errorLevel;
    private readonly Action<ErrorBudgetReport>? _onBudgetExhausted;
    private readonly Action<ErrorBudgetReport>? _onBudgetRecovered;

    private long _totalEvents;
    private long _errorEvents;
    private long _windowStartTicks;
    private int _budgetExhausted; // 0 = healthy, 1 = exhausted

    /// <param name="sloTarget">SLO target as a fraction (e.g., 0.999 for 99.9%).</param>
    /// <param name="windowSeconds">Rolling window size in seconds (default: 300 = 5 minutes).</param>
    /// <param name="errorLevel">Level at or above which events count as errors (default: Error).</param>
    /// <param name="levelRegistry">Level registry for level comparison. If null, uses string comparison against "error".</param>
    /// <param name="onBudgetExhausted">Fired once when error rate exceeds budget. Not fired again until recovery.</param>
    /// <param name="onBudgetRecovered">Fired once when error rate drops back within budget after exhaustion.</param>
    public ErrorBudgetMonitor(
        double sloTarget = 0.999,
        int windowSeconds = 300,
        LogLevel? errorLevel = null,
        ILogLevelRegistry? levelRegistry = null,
        Action<ErrorBudgetReport>? onBudgetExhausted = null,
        Action<ErrorBudgetReport>? onBudgetRecovered = null)
    {
        if (sloTarget is <= 0.0 or >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(sloTarget),
                "SLO target must be between 0 and 1 exclusive (e.g., 0.999).");

        if (windowSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(windowSeconds),
                "Window must be positive.");

        _sloTarget = sloTarget;
        _errorBudgetFraction = 1.0 - sloTarget;
        _windowTicks = TimeSpan.FromSeconds(windowSeconds).Ticks;
        _errorLevel = errorLevel ?? KnownLogLevels.Error;
        _levelRegistry = levelRegistry;
        _onBudgetExhausted = onBudgetExhausted;
        _onBudgetRecovered = onBudgetRecovered;
        _windowStartTicks = DateTimeOffset.UtcNow.Ticks;
    }

    public LogEvent? Process(LogEvent logEvent)
    {
        MaybeRotateWindow();

        Interlocked.Increment(ref _totalEvents);

        if (IsError(logEvent))
            Interlocked.Increment(ref _errorEvents);

        CheckBudget();

        return logEvent;
    }

    /// <summary>Current error budget report.</summary>
    public ErrorBudgetReport GetReport()
    {
        var total = Interlocked.Read(ref _totalEvents);
        var errors = Interlocked.Read(ref _errorEvents);
        var errorRate = total > 0 ? (double)errors / total : 0.0;
        var burnRate = _errorBudgetFraction > 0 ? errorRate / _errorBudgetFraction : 0.0;
        var budgetRemaining = Math.Max(0.0, 1.0 - burnRate);

        return new ErrorBudgetReport(
            SloTarget: _sloTarget,
            ErrorRate: errorRate,
            BurnRate: Math.Round(burnRate, 3),
            BudgetRemaining: Math.Round(budgetRemaining, 3),
            TotalEvents: total,
            ErrorEvents: errors,
            IsExhausted: Volatile.Read(ref _budgetExhausted) == 1);
    }

    private bool IsError(LogEvent logEvent)
    {
        if (_levelRegistry is not null)
            return _levelRegistry.IsAtOrAbove(logEvent.Level, _errorLevel);

        // Fallback: string comparison
        return string.Equals(logEvent.Level.Key, _errorLevel.Key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(logEvent.Level.Key, "fatal", StringComparison.OrdinalIgnoreCase);
    }

    private void CheckBudget()
    {
        var total = Interlocked.Read(ref _totalEvents);
        if (total < 10) return; // Need minimum sample size

        var errors = Interlocked.Read(ref _errorEvents);
        var errorRate = (double)errors / total;
        var burnRate = _errorBudgetFraction > 0 ? errorRate / _errorBudgetFraction : 0.0;

        if (burnRate >= 1.0)
        {
            // Budget exhausted
            if (Interlocked.CompareExchange(ref _budgetExhausted, 1, 0) == 0)
                _onBudgetExhausted?.Invoke(GetReport());
        }
        else
        {
            // Budget recovered
            if (Interlocked.CompareExchange(ref _budgetExhausted, 0, 1) == 1)
                _onBudgetRecovered?.Invoke(GetReport());
        }
    }

    private void MaybeRotateWindow()
    {
        var now = DateTimeOffset.UtcNow.Ticks;
        var start = Volatile.Read(ref _windowStartTicks);

        if (now - start < _windowTicks)
            return;

        // Window expired — reset counters
        if (Interlocked.CompareExchange(ref _windowStartTicks, now, start) == start)
        {
            Interlocked.Exchange(ref _totalEvents, 0);
            Interlocked.Exchange(ref _errorEvents, 0);
            // Don't reset _budgetExhausted — let CheckBudget handle state transitions
        }
    }
}

/// <summary>
/// Point-in-time snapshot of error budget status.
/// </summary>
public sealed record ErrorBudgetReport(
    double SloTarget,
    double ErrorRate,
    double BurnRate,
    double BudgetRemaining,
    long TotalEvents,
    long ErrorEvents,
    bool IsExhausted)
{
    public override string ToString() =>
        $"SLO {SloTarget:P1} | Error rate {ErrorRate:P3} | Burn {BurnRate:F1}x | Budget {BudgetRemaining:P1} remaining | {ErrorEvents}/{TotalEvents} errors{(IsExhausted ? " [EXHAUSTED]" : "")}";
}
