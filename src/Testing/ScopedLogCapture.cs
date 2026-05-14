#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;

namespace MMP.Herald.Testing;

/// <summary>
/// AsyncLocal-scoped log capture for test isolation.
/// Each test can call BeginCapture() to get its own capture scope,
/// so parallel tests do not interfere with each other.
/// </summary>
public sealed class ScopedLogCapture : ILogger
{
    private static readonly AsyncLocal<CaptureScope?> CurrentScope = new();

    public void Log(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        CurrentScope.Value?.Add(logEvent);
    }

    /// <summary>
    /// Begins a new capture scope. Events logged while the scope is active
    /// are captured and retrievable via GetCapturedEvents().
    /// Dispose the returned scope to end capture and prevent memory leaks.
    /// </summary>
    public static CaptureScope BeginCapture()
    {
        var scope = new CaptureScope();
        CurrentScope.Value = scope;
        return scope;
    }

    /// <summary>
    /// Returns events captured in the current scope, or empty if no scope is active.
    /// </summary>
    public static IReadOnlyList<LogEvent> GetCapturedEvents()
    {
        return CurrentScope.Value?.GetEvents() ?? Array.Empty<LogEvent>();
    }

    /// <summary>
    /// An individual capture scope tied to an async context.
    /// </summary>
    public sealed class CaptureScope : IDisposable
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();
        private bool _isDisposed;

        internal void Add(LogEvent logEvent)
        {
            if (!_isDisposed)
            {
                _events.Enqueue(logEvent);
            }
        }

        public IReadOnlyList<LogEvent> GetEvents()
        {
            return _events.ToList();
        }

        public int Count => _events.Count;

        public void Dispose()
        {
            _isDisposed = true;
            CurrentScope.Value = null;
        }
    }
}
