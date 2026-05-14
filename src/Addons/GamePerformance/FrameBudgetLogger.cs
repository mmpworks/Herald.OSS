#nullable enable

using System;
using System.Diagnostics;
using System.Threading;
using MMP.Herald.Events;
using MMP.Herald.Failures;
using MMP.Herald.Pipeline;

namespace MMP.Herald.Addons.GamePerformance;

/// <summary>
/// Tracks the time budget consumed by logging per frame/window.
/// When the budget is exceeded, events are dropped for the remainder
/// of the current window to protect frame time.
///
/// Call ResetBudget() at the start of each frame/tick to reset the counter.
///
/// Usage:
///   var budget = new FrameBudgetLogger(inner, budgetMs: 0.5, failureSink);
///   // In game loop:
///   budget.ResetBudget();
///   logger.Info(...); // tracked against budget
///   logger.Debug(...); // dropped if budget exceeded
/// </summary>
public sealed class FrameBudgetLogger : ILogger, MMP.Herald.Pipeline.IComponentMetadata
{
    private readonly ILogger _inner;
    private readonly ILogFailureSink _failureSink;
    private readonly long _budgetTicks;
    private readonly Action<double>? _onBudgetExceeded;

    private long _windowStartTicks;
    private long _elapsedTicks;
    private int _droppedCount;

    public FrameBudgetLogger(
        ILogger inner,
        double budgetMs,
        ILogFailureSink? failureSink = null,
        Action<double>? onBudgetExceeded = null) {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _failureSink = failureSink ?? NullLogFailureSink.Instance;
        _onBudgetExceeded = onBudgetExceeded;

        if (budgetMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(budgetMs), "Budget must be positive.");
        }

        _budgetTicks = (long)(budgetMs * Stopwatch.Frequency / 1000.0);
        _windowStartTicks = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Reset the budget counter. Call at the start of each frame/tick.
    /// </summary>
    public void ResetBudget() {
        Volatile.Write(ref _windowStartTicks, Stopwatch.GetTimestamp());
        Volatile.Write(ref _elapsedTicks, 0);
        Interlocked.Exchange(ref _droppedCount, 0);
    }

    /// <summary>
    /// Number of events dropped in the current window due to budget exhaustion.
    /// </summary>
    public int DroppedInCurrentWindow => Volatile.Read(ref _droppedCount);

    /// <summary>
    /// Elapsed milliseconds consumed by logging in the current window.
    /// </summary>
    public double ElapsedMs =>
        Volatile.Read(ref _elapsedTicks) * 1000.0 / Stopwatch.Frequency;

    public void Log(LogEvent logEvent) {
        ArgumentNullException.ThrowIfNull(logEvent);

        var currentElapsed = Volatile.Read(ref _elapsedTicks);
        if (currentElapsed >= _budgetTicks)
        {
            // Budget exceeded - drop event
            Interlocked.Increment(ref _droppedCount);
            _failureSink.ReportFailure(logEvent,
                new InvalidOperationException("Frame budget exceeded. Event dropped."),
                nameof(FrameBudgetLogger));
            return;
        }

        var start = Stopwatch.GetTimestamp();

        _inner.Log(logEvent);

        var elapsed = Stopwatch.GetTimestamp() - start;
        var newTotal = Interlocked.Add(ref _elapsedTicks, elapsed);

        if (newTotal >= _budgetTicks && currentElapsed < _budgetTicks)
        {
            // Just crossed the threshold
            var totalMs = newTotal * 1000.0 / Stopwatch.Frequency;
            _onBudgetExceeded?.Invoke(totalMs);
        }
    }

    // -- IComponentMetadata --
    string Pipeline.IComponentMetadata.ComponentName => "frameBudget";
    string Pipeline.IComponentMetadata.DisplayName => "Frame Budget";
    string Pipeline.IComponentMetadata.Description => "Tracks logging time per frame; drops events when budget exceeded.";
    string Pipeline.IComponentMetadata.Help => "Call ResetBudget() at frame start. Events are dropped once cumulative logging time exceeds budgetMs. Callback fires on budget exhaustion.";
    Pipeline.VendorInfo Pipeline.IComponentMetadata.Vendor => Pipeline.VendorInfo.MMP;
    internal static readonly System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> DefaultSchema =
    [
        Routing.SinkConfigField.Int("budgetTicks", 0, "Per-frame time budget in ticks",
            "Maximum Stopwatch ticks allowed for logging per frame. Once exceeded, remaining events in that frame are dropped. At 60 FPS you have ~166,667 ticks per frame (10MHz timer). Set based on your target frame rate and acceptable logging overhead."),
    ];
    System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> Pipeline.IComponentMetadata.ConfigurationSchema =>
    [
        DefaultSchema[0] with { DefaultValue = (int)_budgetTicks },
    ];
}
