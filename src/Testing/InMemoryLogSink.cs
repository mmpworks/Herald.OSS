#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;

namespace MMP.Herald.Testing;

/// <summary>
/// Thread-safe in-memory log sink for unit testing.
/// Captures all log events for later inspection and assertion.
/// </summary>
public sealed class InMemoryLogSink : ILogger
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public void Log(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        _events.Enqueue(logEvent);
    }

    /// <summary>
    /// Returns a snapshot of all captured events.
    /// </summary>
    public IReadOnlyList<LogEvent> GetEvents()
    {
        return _events.ToList();
    }

    /// <summary>
    /// Returns the number of captured events.
    /// </summary>
    public int Count => _events.Count;

    /// <summary>
    /// Removes all captured events.
    /// </summary>
    public void Clear()
    {
        while (_events.TryDequeue(out _)) { }
    }
}
