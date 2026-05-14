#nullable enable

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MMP.Herald.Events;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Kernel-aware async sink wrapper. Stays on the kernel fast path —
/// the kernel hands the buffer directly to <see cref="Log(in LogEventBuffer)"/>;
/// the producer materialises one heap <see cref="LogEvent"/> via
/// <see cref="LogEventBuffer.ToLogEvent"/>, enqueues it on a bounded
/// <see cref="Channel{T}"/>, and returns. A background task drains
/// the channel and forwards events to the inner sink.
///
/// <para>
/// <b>Why this exists.</b> Today's <see cref="AsyncLogger"/> is a chain
/// decorator — registering it via <see cref="Quick.QuickLogBuilder.WithAsyncLogging"/>
/// disqualifies the kernel fast path entirely (the "async" strategy
/// step is intolerable to <see cref="KernelEligibility"/> when its
/// policy is enabled). A pipeline whose only async-related need is
/// "wrap this sink so the producer doesn't block on it" does not need
/// to leave the kernel. This wrapper sits at the sink boundary
/// instead — the kernel fans out to it directly.
/// </para>
///
/// <para>
/// <b>Cost shape.</b> Different from the in-pipeline transforms
/// (redactor, sampler, enricher, dynamic level). Async is a producer/
/// consumer split, not a per-event predicate; the producer always
/// materialises to a heap-safe representation before enqueuing. The
/// recovery vs the chain decorator is bounded by "what does the chain
/// add over a direct sink-wrapper" — primarily the chain's per-event
/// allocation overhead and one decorator dispatch.
/// </para>
///
/// <para>
/// <b>Drop-on-overflow.</b> When the channel is full, <see cref="Channel{T}.Writer"/>
/// rejects the write and the event is dropped. A real production
/// implementation would also notify a drop sink; this experimental
/// wrapper just counts drops for the bench's reporting purposes.
/// </para>
///
/// <para>
/// <b>Drain semantics for hot-reload.</b> <see cref="DrainAsync"/>
/// completes the channel writer and waits for the consumer to flush
/// every queued event into the inner sink within a bounded timeout.
/// The hot-reload path uses this to retire the old sink instance
/// after installing the new one: in-flight events finish on the old
/// inner sink, post-swap events route through the new instance, and
/// no event is lost during the swap. Drain is idempotent — calling
/// it again on an already-drained sink completes immediately.
/// </para>
/// </summary>
public sealed class FastPathAsyncSink : ILogger, IKernelSink, IAsyncDisposable
{
    private readonly ILogger _inner;
    private readonly Channel<LogEvent> _channel;
    private readonly CancellationTokenSource _cts;
    private readonly Task _consumer;
    private long _droppedCount;
    private long _writtenCount;
    private volatile bool _disposed;

    public FastPathAsyncSink(ILogger inner, int boundedCapacity = 1024)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (boundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(boundedCapacity), boundedCapacity,
                "Bounded capacity must be greater than zero.");
        }

        _inner = inner;
        _channel = Channel.CreateBounded<LogEvent>(new BoundedChannelOptions(boundedCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });
        _cts = new CancellationTokenSource();
        _consumer = Task.Run(ConsumeAsync);
    }

    /// <summary>Total events successfully enqueued.</summary>
    public long WrittenCount => Interlocked.Read(ref _writtenCount);

    /// <summary>Total events dropped because the channel was full.</summary>
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    /// <summary>
    /// Kernel-path entry. Materialise the buffer to a heap event,
    /// enqueue, return. The materialisation cost is the dominant piece
    /// of the producer's per-call work — we cannot enqueue a ref struct.
    /// </summary>
    public void Log(in LogEventBuffer buffer)
    {
        var evt = buffer.ToLogEvent();
        if (_channel.Writer.TryWrite(evt))
        {
            Interlocked.Increment(ref _writtenCount);
        }
        else
        {
            Interlocked.Increment(ref _droppedCount);
        }
    }

    /// <summary>
    /// Chain-path entry. Same shape as the kernel path minus the
    /// materialisation step (the chain has already built the event).
    /// </summary>
    public void Log(LogEvent logEvent)
    {
        if (_channel.Writer.TryWrite(logEvent))
        {
            Interlocked.Increment(ref _writtenCount);
        }
        else
        {
            Interlocked.Increment(ref _droppedCount);
        }
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var evt in _channel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                try
                {
                    _inner.Log(evt);
                }
                catch
                {
                    // Swallow — the consumer thread cannot leak exceptions
                    // back to the producer. A real implementation would
                    // route through a failure sink.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    /// <summary>
    /// Default drain timeout used by <see cref="DisposeAsync"/> and the
    /// no-arg <see cref="DrainAsync()"/> overload. Five seconds matches
    /// the original DisposeAsync wait — long enough for a healthy inner
    /// sink to process a backlog, short enough that a hung sink does not
    /// block a hot-reload indefinitely.
    /// </summary>
    public static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Complete the channel writer and wait for the consumer to flush
    /// every queued event into the inner sink. Returns <c>true</c> when
    /// the consumer drained successfully within <paramref name="timeout"/>;
    /// <c>false</c> when the wait timed out (the consumer is still alive,
    /// the events still in flight). Callers that need a hard cutoff after
    /// timeout should follow up with <see cref="DisposeAsync"/>, which
    /// cancels the consumer if drain timed out.
    ///
    /// <para>
    /// <b>Idempotent.</b> Subsequent calls return immediately because the
    /// writer is already completed and the consumer task is already
    /// terminal. After drain completes, further <see cref="Log(in LogEventBuffer)"/>
    /// calls are silent drops — the writer rejects them and the dropped
    /// counter increments. Operationally this is the right shape: a
    /// drained sink should not pretend events succeeded.
    /// </para>
    /// </summary>
    public async ValueTask<bool> DrainAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        // TryComplete is idempotent — first caller wins, later callers
        // observe false but it does not throw. Safe to call from any
        // thread, any number of times.
        _channel.Writer.TryComplete();

        try
        {
            await _consumer.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Drain with the <see cref="DefaultDrainTimeout"/>. Same contract
    /// as the timeout overload otherwise.
    /// </summary>
    public ValueTask<bool> DrainAsync() =>
        DrainAsync(DefaultDrainTimeout, CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Drain first; if it times out, fall through to cancellation so
        // the consumer task does not pin the instance forever.
        if (await DrainAsync(DefaultDrainTimeout, CancellationToken.None).ConfigureAwait(false))
        {
            _cts.Dispose();
            return;
        }

        _cts.Cancel();
        try { await _consumer.ConfigureAwait(false); } catch { }
        _cts.Dispose();
    }
}
