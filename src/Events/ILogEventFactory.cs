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
}