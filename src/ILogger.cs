#nullable enable

using System.Threading;
using System.Threading.Tasks;
using MMP.Herald.Events;

namespace MMP.Herald;

/// <summary>
/// Low-level pipeline port for fully-formed log events.
///
/// <para>
/// Implementers must provide <see cref="Log"/>. <see cref="LogAsync"/> has a
/// default implementation that calls <see cref="Log"/> synchronously, so any
/// existing <see cref="ILogger"/> keeps working without change. Sinks and
/// decorators that can do real async work — HTTP, TCP, OTLP — override
/// <see cref="LogAsync"/> to use the async transport primitives end-to-end.
/// </para>
///
/// <para>
/// <see cref="Pipeline.AsyncLogger"/> drains its background queue through
/// <see cref="LogAsync"/>, so overriding the method on network sinks lets
/// the worker thread yield while the socket completes instead of blocking.
/// </para>
/// </summary>
public interface ILogger
{
    /// <summary>
    /// Accept a log event. Synchronous entry point used by the callers that
    /// still issue events on their own thread. Every pipeline component must
    /// implement this; it stays the canonical path for in-process producers.
    /// </summary>
    void Log(LogEvent logEvent);

    /// <summary>
    /// Accept a log event on an async worker. Default implementation delegates
    /// to <see cref="Log"/> and returns a completed task, which matches the
    /// pre-existing behavior. Network-bound sinks override this so the
    /// background drain does not block a worker thread on socket I/O.
    /// </summary>
    ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
    {
        Log(logEvent);
        return ValueTask.CompletedTask;
    }
}
