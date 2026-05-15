#nullable enable

using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using MMP.Herald.Addons.Query;
using MMP.Herald.Events;
using MMP.Herald.Formatting;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline.Kernel;

namespace MMP.Herald.Addons.Archive;

/// <summary>
/// Terminal sink that streams matching events to an
/// <see cref="IStreamingArchiveSession"/>. Each accepted event is
/// serialised as a UTF-8 JSON object followed by a newline (NDJSON), then
/// handed to the session for buffered remote append.
///
/// <para>
/// <b>DSL filtering.</b> When the <see cref="StreamingArchivePolicy.Predicate"/>
/// is set, the expression compiles once via <see cref="LogEventQuery.Parse"/>
/// at construction and evaluates on the hot path. Events that do not match
/// are silently skipped — the decorator is a filter plus a sink, not a
/// tee. Operators who want both filtered streaming and pass-through logging
/// wire the streaming sink alongside their regular sinks in the fan-out.
/// </para>
///
/// <para>
/// <b>Failure posture.</b> Streaming archive is best-effort backup. The
/// session swallows transient remote failures with a stderr note and keeps
/// buffered events in memory; the closed-file archive path remains the
/// durable record. An HMAC audit consumer would not use streaming.
/// </para>
///
/// <para>
/// <b>Lifecycle.</b> The logger owns the session. <see cref="DisposeAsync"/>
/// flushes buffered events and closes the remote object. A process kill
/// between flushes loses the in-flight buffer; that is the trade for
/// streaming vs. closed-file.
/// </para>
/// </summary>
public sealed class StreamingArchiveLogger : ILogger, IKernelSink, IAsyncDisposable
{
    private readonly IStreamingArchiveSession _session;
    private readonly Utf8JsonFormatter _formatter;
    private readonly LogEventQuery? _predicate;
    private bool _disposed;

    /// <summary>
    /// Build the logger. When <paramref name="policy"/> carries a
    /// <see cref="StreamingArchivePolicy.Predicate"/>, the query compiles
    /// at construction and is invoked per event.
    /// </summary>
    public StreamingArchiveLogger(
        IStreamingArchiveSession session,
        StreamingArchivePolicy policy,
        ILogLevelRegistry levelRegistry)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(levelRegistry);

        _session = session;
        _formatter = new Utf8JsonFormatter(levelRegistry);
        _predicate = CompilePredicate(policy.Predicate);
    }

    /// <summary>
    /// The streaming session this logger writes to. Exposed for operator
    /// tooling that wants to surface the <see cref="IStreamingArchiveSession.RemoteIdentifier"/>
    /// in diagnostics.
    /// </summary>
    public IStreamingArchiveSession Session => _session;

    public void Log(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        if (_disposed) return;
        if (!Matches(logEvent)) return;

        var bytes = SerializeEvent(logEvent);
        // Fire-and-forget on the sync path; the session's internal channel
        // queues the bytes and returns immediately. The drain task handles
        // the remote write off the caller thread.
        _ = _session.AppendAsync(bytes, CancellationToken.None);
    }

    // Kernel-path entry. Predicate eval + NDJSON serialisation both read the
    // rendered Message, so materialise + render at the boundary and forward
    // to the heap path.
    public void Log(in LogEventBuffer buffer)
    {
        if (_disposed) return;
        Log(KernelBufferAdapter.MaterializeAndRender(in buffer));
    }

    public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        if (_disposed) return ValueTask.CompletedTask;
        if (!Matches(logEvent)) return ValueTask.CompletedTask;

        var bytes = SerializeEvent(logEvent);
        return new ValueTask(_session.AppendAsync(bytes, cancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _session.DisposeAsync().ConfigureAwait(false);
    }

    private bool Matches(LogEvent logEvent) =>
        _predicate is null || _predicate.Matches(logEvent);

    /// <summary>
    /// Render the event as a UTF-8 JSON object plus a trailing newline.
    /// One small heap allocation per event — the pooled buffer writer is
    /// copied into a right-sized array because the session's channel
    /// captures the memory for later drain, and we cannot hand the pooled
    /// array back to the pool while the channel still holds a reference.
    /// </summary>
    private ReadOnlyMemory<byte> SerializeEvent(LogEvent logEvent)
    {
        var writer = new ArrayBufferWriter<byte>(initialCapacity: 512);
        _formatter.Format(logEvent, writer);
        writer.Write("\n"u8);

        var span = writer.WrittenSpan;
        var owned = new byte[span.Length];
        span.CopyTo(owned);
        return owned;
    }

    private static LogEventQuery? CompilePredicate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // Throws QueryParseException on malformed input. Intentional —
        // fail fast at construction, not silently at runtime.
        return LogEventQuery.Parse(raw!);
    }
}
