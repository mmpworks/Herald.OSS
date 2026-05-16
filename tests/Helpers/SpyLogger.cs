#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;

namespace MMP.Herald.OSS.Tests.Helpers;

/// <summary>
/// A thread-safe logger that records all events and optionally throws on Log().
/// </summary>
public sealed class SpyLogger : ILogger, IBatchedLogSink
{
    private readonly List<LogEvent> _events = new();
    private readonly List<IReadOnlyList<LogEvent>> _batches = new();
    private readonly object _sync = new();
    private Exception? _throwOnLog;

    public IReadOnlyList<LogEvent> Events { get { lock (_sync) return new List<LogEvent>(_events); } }
    public IReadOnlyList<IReadOnlyList<LogEvent>> Batches { get { lock (_sync) return new List<IReadOnlyList<LogEvent>>(_batches); } }
    public int LogCount { get { lock (_sync) return _events.Count; } }
    public int BatchCount { get { lock (_sync) return _batches.Count; } }

    public void ThrowOnLog(Exception exception) => _throwOnLog = exception;

    public void Log(LogEvent logEvent)
    {
        if (_throwOnLog is not null)
        {
            throw _throwOnLog;
        }

        lock (_sync)
        {
            _events.Add(logEvent);
        }
    }

    public void LogBatch(IReadOnlyList<LogEvent> events)
    {
        lock (_sync)
        {
            _batches.Add(events);
            foreach (var e in events)
            {
                _events.Add(e);
            }
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _events.Clear();
            _batches.Clear();
        }
    }
}
