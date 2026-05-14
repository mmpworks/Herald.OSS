#nullable enable

using System.Collections.Generic;
using MMP.Herald.Levels;
using MMP.Herald.Templating;

namespace MMP.Herald.Events;

/// <summary>
/// Creates log events using injected infrastructure such as time, scoped context,
/// enrichment, and message template rendering.
/// </summary>
public interface ILogEventFactory
{
    LogEvent Create(
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        IReadOnlyList<LogProperty>? properties = null,
        IReadOnlyDictionary<string, object?>? defaultContext = null,
        IReadOnlyDictionary<string, object?>? context = null,
        LogEventId? eventId = null);

    /// <summary>
    /// Provenance stamp this factory writes onto every event. The pipeline's
    /// <c>StructuredLogger</c> reads this at construction and uses the same
    /// value when stamping kernel-path <c>LogEventBuffer</c>s, so chain-path
    /// and kernel-path events from the same pipeline carry identical
    /// <c>GenSource</c> values. Sinks hoisted into the pipeline store this
    /// in their accepted-source list. Default implementations return null
    /// to opt out of gating.
    /// </summary>
    string? GenSource => null;
}