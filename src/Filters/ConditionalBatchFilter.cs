#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using MMP.Herald.Events;
using MMP.Herald.Predicates;

namespace MMP.Herald.Filters;

/// <summary>
/// Batch filter inspired by NLog's PostFilteringTargetWrapper.
/// Scans the batch for any event matching the trigger condition.
/// If triggered, applies the escalated filter to all events (keeping more detail).
/// Otherwise, applies the normal filter (keeping only important events).
///
/// Example: normalFilter = Warn+, escalatedFilter = Debug+, trigger = Error.
/// Result: normally only Warn+ events reach sinks, but if any Error is in the batch,
/// Debug+ events from the same batch are also forwarded for context.
/// </summary>
public sealed class ConditionalBatchFilter : IBatchLogFilter
{
    private readonly ILogPredicate _triggerCondition;
    private readonly ILogPredicate _normalFilter;
    private readonly ILogPredicate _escalatedFilter;

    public ConditionalBatchFilter(
        ILogPredicate triggerCondition,
        ILogPredicate normalFilter,
        ILogPredicate escalatedFilter)
    {
        _triggerCondition = triggerCondition ?? throw new ArgumentNullException(nameof(triggerCondition));
        _normalFilter = normalFilter ?? throw new ArgumentNullException(nameof(normalFilter));
        _escalatedFilter = escalatedFilter ?? throw new ArgumentNullException(nameof(escalatedFilter));
    }

    public IReadOnlyList<LogEvent> FilterBatch(IReadOnlyList<LogEvent> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Count == 0)
        {
            return batch;
        }

        var isTriggered = batch.Any(e => _triggerCondition.Evaluate(e));
        var activeFilter = isTriggered ? _escalatedFilter : _normalFilter;

        return batch.Where(e => activeFilter.Evaluate(e)).ToList();
    }
}
