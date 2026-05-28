#nullable enable

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MMP.Herald.Events;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Quick;
using MMP.Herald.Templating;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Kernel-aware async sink wrapper. Stays on the kernel fast path —
/// the kernel hands the buffer directly to <see cref="Log(in LogEventBuffer)"/>;
/// the producer constructs an inline <c>AsyncEnvelope</c> value on its stack,
/// copies it into a bounded <see cref="Channel{T}"/>, and returns. A background
/// task drains the channel and forwards events to the inner sink.
///
/// <para>
/// <b>Lever A — inline envelope handoff.</b> The producer-side payload is
/// the inline <c>AsyncEnvelope</c> struct: a header + <c>[InlineArray(8)]</c>
/// slot buffer + optional overflow array. Zero per-event heap allocation on
/// the producer when arity ≤ 8 (the 99%+ case); one overflow array when
/// arity > 8. The earlier shape carried a heap <see cref="LogEvent"/>
/// through <c>Channel&lt;LogEvent&gt;</c> at ~296 B/event. Design rationale
/// and re-measure data:
/// <c>docs/_wip/lever-a-paced-remeasure-2026-05-27/async-handoff-design-space-catalog.md</c>.
/// </para>
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
/// <b>Drain reconstitution.</b> The consumer reads each envelope and
/// reconstitutes for the inner sink: when the inner implements
/// <see cref="IKernelSink"/>, the drain builds a <see cref="LogEventBuffer"/>
/// on its own stack (via an <c>[InlineArray(8)]</c> local) and hands it
/// to <see cref="IKernelSink.Log(in LogEventBuffer)"/> — truly 0-alloc
/// system-wide. When the inner is legacy-only, the drain materialises a
/// heap <see cref="LogEvent"/> at the boundary and forwards via
/// <see cref="ILogger.Log(LogEvent)"/> — the allocation moves from the
/// producer thread to the drain thread, where it amortises behind the
/// single-reader without producer contention.
/// </para>
///
/// <para>
/// <b>Lazy-resolution contract.</b> The producer eagerly resolves
/// <see cref="LogProperty.ResolvedValue"/> for any property whose
/// <see cref="LogProperty.Value"/> is a <see cref="Func{T}"/> at envelope
/// construction time. The factory runs on the producer thread while
/// the original async-flow context (tenant scope, HttpContext,
/// AsyncLocal&lt;T&gt;) is still in effect. Deferring resolution to
/// the drain thread is the cross-tenant PII vector closed in 0.10.2;
/// see the security posture link in the CHANGELOG.
/// </para>
///
/// <para>
/// <b>Cost shape.</b> Different from the in-pipeline transforms
/// (redactor, sampler, enricher, dynamic level). Async is a producer/
/// consumer split, not a per-event predicate; the producer always
/// materialises to a value-typed envelope before enqueuing.
/// </para>
///
/// <para>
/// <b>Drop-on-overflow.</b> When the channel is full, <see cref="Channel{T}.Writer"/>
/// rejects the write and the event is dropped. The wrapper counts drops
/// for the bench's reporting purposes.
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
    private readonly IKernelSink? _innerKernel;
    private readonly Channel<AsyncEnvelope> _channel;
    private readonly CancellationTokenSource _cts;
    private readonly Task _consumer;

    // L3 of the async-sink cross-tenant PII fix (0.10.2): capture the
    // tenant scope at CONSTRUCTION time. FastPathAsyncSink must be
    // constructed outside any request scope so this captures the
    // "expected" tenant the drain runs as. QuickLogBuilder.Build()
    // already constructs at bootstrap; the drain-entry assertion below
    // verifies the invariant held.
    private readonly string _constructionTenant;

    // L5 of the async-sink cross-tenant PII fix (0.10.2): structured
    // failure-sink hookup for drain errors. Replaces the prior empty
    // catch — drain errors now surface via _failureSink synchronously
    // (bypassing the possibly-sick async path) carrying GenSource +
    // exception type but NOT the original Properties (no PII through
    // the error path). Null when no failure sink was wired; in that
    // case drain errors still don't crash the consumer thread, they
    // just go unreported (same observable behaviour as 0.10.1).
    private readonly ILogFailureSink? _failureSink;

    private long _droppedCount;
    private long _writtenCount;
    private long _drainErrorCount;
    private volatile bool _disposed;

    public FastPathAsyncSink(
        ILogger inner,
        int boundedCapacity = 1024,
        ILogFailureSink? failureSink = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (boundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(boundedCapacity), boundedCapacity,
                "Bounded capacity must be greater than zero.");
        }

        _inner = inner;
        // Inner-sink kind decides the drain reconstitution path. We cache
        // the cast once at construction so the drain loop's branch is a
        // single null-check per event instead of a per-event `as`.
        _innerKernel = inner as IKernelSink;
        _failureSink = failureSink;
        // Capture the construction-time tenant once. Subsequent drain-entry
        // assertions compare against this baseline — a mismatch means the
        // sink was constructed inside a request scope (operator error) and
        // the drain would inherit a non-default tenant.
        _constructionTenant = HeraldTenantScope.Current;
        _channel = Channel.CreateBounded<AsyncEnvelope>(new BoundedChannelOptions(boundedCapacity)
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
    /// Total drain-side errors observed while forwarding events to the
    /// inner sink. Increments once per failed forward; the corresponding
    /// failure is also surfaced through <c>ILogFailureSink</c> when one is
    /// wired. Operators can poll this counter as a cheap health signal
    /// without needing a failure-sink subscription.
    /// </summary>
    public long DrainErrorCount => Interlocked.Read(ref _drainErrorCount);

    /// <summary>
    /// The tenant scope this sink was constructed under. The drain entry
    /// asserts the consumer-thread tenant matches this baseline before
    /// processing each event; a mismatch indicates the sink was created
    /// inside a request scope (an operator error) and is reported once
    /// via the failure-sink hookup.
    /// </summary>
    public string ConstructionTenant => _constructionTenant;

    /// <summary>
    /// Kernel-path entry. Materialise the buffer to an inline value-typed
    /// envelope on the producer's stack, enqueue, return. Zero heap on the
    /// producer when arity ≤ 8; one overflow array when arity > 8. The
    /// envelope ride is by-value into the channel slot.
    /// </summary>
    public void Log(in LogEventBuffer buffer)
    {
        var envelope = new AsyncEnvelope(in buffer);
        if (_channel.Writer.TryWrite(envelope))
        {
            Interlocked.Increment(ref _writtenCount);
        }
        else
        {
            Interlocked.Increment(ref _droppedCount);
        }
    }

    /// <summary>
    /// Chain-path entry. Wraps the heap event in the inline envelope so the
    /// drain code uniformly handles one shape. The eager-resolution pass
    /// happens here on the producer thread (the chain caller's thread),
    /// preserving the lazy-resolution contract.
    /// </summary>
    public void Log(LogEvent logEvent)
    {
        var envelope = new AsyncEnvelope(logEvent);
        if (_channel.Writer.TryWrite(envelope))
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
        // L3 of the async-sink cross-tenant PII fix (0.10.2): assert the
        // drain thread's ambient tenant matches the construction-time
        // tenant. The drain thread inherits the ExecutionContext at
        // Task.Run time, which is the bootstrap context; if a future
        // refactor accidentally lifts FastPathAsyncSink construction
        // into a request scope, the assertion catches the drift the
        // first time the drain runs. Defense-in-depth — the L1/L2
        // eager-resolution layers already prevent the actual leak;
        // this catches the structural error that would degrade them.
        if (!string.Equals(HeraldTenantScope.Current, _constructionTenant, StringComparison.Ordinal))
        {
            ReportDrainEntryTenantMismatch();
        }

        try
        {
            await foreach (var envelope in _channel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                try
                {
                    ForwardEnvelope(envelope);
                }
                catch (Exception ex)
                {
                    // L5 of the async-sink cross-tenant PII fix (0.10.2):
                    // structured fail-loud, never drop. We DO NOT re-throw
                    // (would starve the sink) and we DO NOT include the
                    // envelope's original properties in the failure event
                    // (no PII propagation through the error path). The
                    // diagnostic carries the envelope's GenSource (audit
                    // context) + the exception type, nothing else.
                    Interlocked.Increment(ref _drainErrorCount);
                    ReportDrainForwardError(envelope, ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    // Build the smallest event that lets the failure-sink trace which
    // pipeline failed without leaking property contents. GenSource carries
    // the pipeline-provenance token; nothing else does.
    private void ReportDrainEntryTenantMismatch()
    {
        if (_failureSink is null) return;
        var diagnostic = new LogEvent(
            TimeUtc: DateTimeOffset.UtcNow,
            Level: KnownLogLevels.Error,
            Category: LogCategory.None,
            MessageTemplate: "FastPathAsyncSink drain-entry tenant mismatch",
            Message: "FastPathAsyncSink drain-entry tenant mismatch",
            Properties: LogEvent.EmptyProperties,
            Context: LogEvent.EmptyContext,
            TenantId: HeraldTenantScope.Current);
        var ex = new InvalidOperationException(
            $"FastPathAsyncSink drain thread tenant '{HeraldTenantScope.Current}' " +
            $"does not match the construction-time tenant '{_constructionTenant}'. " +
            "The sink must be constructed outside any request scope.");
        try { _failureSink.ReportFailure(diagnostic, ex, source: "FastPathAsyncSink.L3"); }
        catch { /* failure-sink itself failed; swallow to keep drain alive */ }
    }

    private void ReportDrainForwardError(in AsyncEnvelope envelope, Exception ex)
    {
        if (_failureSink is null) return;
        // Carry only audit context — GenSource + envelope.TimeUtc + Level
        // + Category + a fixed message. No Properties span the boundary.
        var diagnostic = new LogEvent(
            TimeUtc: envelope.TimeUtc,
            Level: envelope.Level,
            Category: envelope.Category,
            MessageTemplate: "FastPathAsyncSink drain forward failed",
            Message: "FastPathAsyncSink drain forward failed",
            Properties: LogEvent.EmptyProperties,
            Context: LogEvent.EmptyContext,
            EventId: envelope.EventId,
            GenSource: envelope.GenSource,
            TenantId: _constructionTenant);
        try { _failureSink.ReportFailure(diagnostic, ex, source: "FastPathAsyncSink.L5"); }
        catch { /* failure-sink itself failed; swallow to keep drain alive */ }
    }

    // Forward one envelope to the inner sink. Splits on inner-sink kind:
    //   IKernelSink -> reconstitute LogEventBuffer on the drain's own stack
    //                  via an [InlineArray(8)] local; truly 0-alloc.
    //   else        -> materialise a heap LogEvent; the allocation moves
    //                  to the drain thread (alloc relocated, not removed).
    private void ForwardEnvelope(in AsyncEnvelope envelope)
    {
        if (_innerKernel is not null)
        {
            ForwardAsBuffer(in envelope);
        }
        else
        {
            ForwardAsHeapEvent(in envelope);
        }
    }

    private void ForwardAsBuffer(in AsyncEnvelope envelope)
    {
        if (envelope.Count <= 8)
        {
            LogPropertyDrainBuffer8 stackBuf = default;
            var props = ((Span<LogProperty>)stackBuf).Slice(0, envelope.Count);
            envelope.WriteSlotsTo(props);
            var buffer = new LogEventBuffer(
                envelope.TimeUtc, envelope.Level, envelope.Category,
                envelope.MessageTemplate, envelope.Message, props,
                envelope.EventId, envelope.GenSource);
            _innerKernel!.Log(in buffer);
        }
        else
        {
            // Overflow: reconstitute via heap array (rare path, arity > 8).
            var heapProps = envelope.ReconstituteProperties();
            var buffer = new LogEventBuffer(
                envelope.TimeUtc, envelope.Level, envelope.Category,
                envelope.MessageTemplate, envelope.Message, heapProps,
                envelope.EventId, envelope.GenSource);
            _innerKernel!.Log(in buffer);
        }
    }

    private void ForwardAsHeapEvent(in AsyncEnvelope envelope)
    {
        var evt = new LogEvent(
            envelope.TimeUtc, envelope.Level, envelope.Category,
            envelope.MessageTemplate, envelope.Message,
            envelope.ReconstituteProperties(), LogEvent.EmptyContext,
            envelope.EventId, CausedBy: null, GenSource: envelope.GenSource);
        _inner.Log(evt);
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
