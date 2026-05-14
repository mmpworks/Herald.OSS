#nullable enable

using System;
using MMP.Herald.Events;

namespace MMP.Herald.Filters;

/// <summary>
/// Adapts a <see cref="Func{LogEvent, Boolean}"/> predicate to the
/// <see cref="ILogFilter"/> contract so callers can hand a lambda
/// directly to the pipeline's filter slot. Plain delegate dispatch —
/// no reflection, no expression-tree compilation, AOT-clean.
/// </summary>
/// <remarks>
/// Use the <c>WithCustomFilter(...)</c> builder shorthand rather than
/// constructing this directly. The builder owns the filter slot and
/// will replace any sampling or expression filter previously installed
/// in the same position.
/// </remarks>
public sealed class FuncLogFilter : ILogFilter
{
    private readonly Func<LogEvent, bool> _predicate;

    public FuncLogFilter(Func<LogEvent, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _predicate = predicate;
    }

    public bool Allow(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        return _predicate(logEvent);
    }
}
