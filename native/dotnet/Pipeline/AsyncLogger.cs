#nullable enable

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MMP.Herald.Events;
using MMP.Herald.Failures;
using MMP.Herald.Metrics;

namespace MMP.Herald.Pipeline;
/// <summary>
/// Asynchronous logger wrapper that decouples callers from slower sinks.
///
/// Features:
/// - Bounded channel with configurable drop strategies (drop_write, drop_oldest, wait)
/// - Configurable drain timeout on shutdown (prevents hangs)
/// - Drop notification callback for backpressure signaling to game loops
/// - Queue depth reporting for health monitoring
/// </summary>
public sealed class AsyncLogger : ILogger, IAsyncDisposable, IDescribable, IComponentMetadata
{
    /// <summary>
    /// Hard upper bound for queue capacity. A config passing
    /// <c>capacity: 1_000_000_000</c> used to allocate a channel large
    /// enough to OOM the process; the cap turns that into a crisp
    /// <see cref="ArgumentOutOfRangeException"/> at construction. Operators
    /// who really need a larger queue opt in via <c>ignoreCapacityCap</c>.
    /// 1 MiB events in-flight is already a very large queue in practice.
    /// </summary>
    public const int MaxAsyncCapacity = 1_048_576;

    /// <summary>
    /// Default sync-wait timeout applied when <see cref="DropStrategy"/> is
    /// <c>wait</c>. Sync callers blocked on a stuck sink used to block
    /// forever; the timeout converts the hang into a <c>SyncWaitTimeout</c>
    /// drop so the caller keeps running.
    /// </summary>
    public static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromMilliseconds(100);

    private readonly ILogger _next;
    private readonly ILogFailureSink _failureSink;
    private readonly Channel<LogEvent> _channel;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Task _processingTask;
    private readonly bool _useBackpressure;
    private readonly TimeSpan? _drainTimeout;
    private readonly TimeSpan _waitTimeout;
    private readonly Action<LogEvent>? _onEventDropped;
    private readonly Action<LogEvent, DropReason>? _onEventDroppedWithReason;
    private readonly IPipelineDropSink _dropSink;
    private volatile bool _isDisposed;

    public AsyncLogger(
        ILogger next,
        ILogFailureSink? failureSink = null,
        int capacity = Services.PipelineDefaults.AsyncCapacity,
        string dropStrategy = Services.KnownDropStrategies.DropWrite,
        TimeSpan? drainTimeout = null,
        Action<LogEvent>? onEventDropped = null,
        TimeSpan? waitTimeout = null,
        Action<LogEvent, DropReason>? onEventDroppedWithReason = null,
        bool ignoreCapacityCap = false,
        IPipelineDropSink? dropSink = null) {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be greater than zero.");
        }
        if (!ignoreCapacityCap && capacity > MaxAsyncCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity,
                $"Capacity {capacity} exceeds MaxAsyncCapacity ({MaxAsyncCapacity}). " +
                $"Set ignoreCapacityCap: true to opt in — the bootstrap logs a warning when you do.");
        }
        if (ignoreCapacityCap && capacity > MaxAsyncCapacity)
        {
            // Operator has explicitly asked for a giant queue. Route a
            // one-line warning to stderr so the decision shows up in the
            // bootstrap audit trail instead of being invisible. Not sent
            // through ILogFailureSink because that interface is shaped
            // around per-event failures, not bootstrap-level notes.
            Console.Error.WriteLine(
                $"[AsyncLogger] capacity {capacity} exceeds MaxAsyncCapacity ({MaxAsyncCapacity}); honoured because ignoreCapacityCap=true.");
        }

        _next = next;
        _failureSink = failureSink ?? NullLogFailureSink.Instance;
        Capacity = capacity;
        DropStrategy = dropStrategy;
        _cancellationTokenSource = new CancellationTokenSource();
        _useBackpressure = dropStrategy.Equals(Services.KnownDropStrategies.Wait, StringComparison.OrdinalIgnoreCase);
        _drainTimeout = drainTimeout;
        _waitTimeout = waitTimeout ?? DefaultWaitTimeout;
        _onEventDropped = onEventDropped;
        _onEventDroppedWithReason = onEventDroppedWithReason;
        _dropSink = dropSink ?? NullPipelineDropSink.Instance;

        // Always use Wait mode so TryWrite returns false when full.
        // This lets our code handle drops (invoke callback, report to failure sink)
        // rather than the channel silently dropping events.
        _channel = Channel.CreateBounded<LogEvent>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        _processingTask = ProcessQueueAsync(_cancellationTokenSource.Token);
    }

    // -- Inspection --

    /// <summary>Current number of events waiting in the queue.</summary>
    public int QueueDepth => _channel.Reader.Count;

    /// <summary>Configured queue capacity.</summary>
    public int Capacity { get; }

    /// <summary>Drop strategy: "drop_write" or "wait".</summary>
    public string DropStrategy { get; }

    /// <summary>The downstream pipeline this logger feeds into.</summary>
    public ILogger Inner => _next;

    /// <summary>
    /// Enqueue a log event for async processing.
    /// With "wait" strategy, blocks until a slot opens using a kernel wait
    /// (no spinning, no sync-over-async). For async callers prefer LogAsync().
    /// With other strategies, events are dropped when the queue is full.
    /// </summary>
    public void Log(LogEvent logEvent) {
        ArgumentNullException.ThrowIfNull(logEvent);

        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_useBackpressure)
        {
            // Sync-wait path with a deadline. A sync caller (Unity Update,
            // Godot _Process, a game server tick) blocked on a stuck
            // downstream sink used to hang forever; we now give up after
            // _waitTimeout and drop the event with SyncWaitTimeout so the
            // caller keeps running. The deadline is measured across the
            // whole retry loop so one call cannot aggregate a multi-second
            // block across successive WaitToWriteAsync rounds.
            //
            // Single linked CTS hoisted above the loop — subsequent
            // TryWrite-lost-the-race iterations reuse it rather than
            // allocating a fresh linked source per retry (audited
            // 2026-04-22 as a pathological-hang allocation footgun).
            // CancelAfter fires once against the wall-clock deadline; the
            // inner loop only checks the pre-computed remaining time to
            // decide when to bail.
            var deadline = Environment.TickCount64 + (long)_waitTimeout.TotalMilliseconds;
            using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
            deadlineCts.CancelAfter(_waitTimeout);
            var deadlineToken = deadlineCts.Token;

            while (true)
            {
                var remaining = deadline - Environment.TickCount64;
                if (remaining <= 0)
                {
                    FireDrop(logEvent, DropReason.SyncWaitTimeout,
                        "AsyncLogger wait-timeout: downstream did not drain within the configured deadline.");
                    return;
                }

                try
                {
                    _channel.Writer.WaitToWriteAsync(deadlineToken).AsTask().Wait(deadlineToken);
                }
                catch (OperationCanceledException) when (!_cancellationTokenSource.IsCancellationRequested)
                {
                    // Deadline hit before a slot opened — treat as timeout.
                    FireDrop(logEvent, DropReason.SyncWaitTimeout,
                        "AsyncLogger wait-timeout: downstream did not drain within the configured deadline.");
                    return;
                }
                catch (AggregateException aex) when (
                    aex.InnerException is OperationCanceledException && !_cancellationTokenSource.IsCancellationRequested)
                {
                    FireDrop(logEvent, DropReason.SyncWaitTimeout,
                        "AsyncLogger wait-timeout: downstream did not drain within the configured deadline.");
                    return;
                }

                if (_isDisposed || _cancellationTokenSource.IsCancellationRequested)
                {
                    return;
                }

                if (_channel.Writer.TryWrite(logEvent))
                {
                    return;
                }
                // TryWrite lost the slot to another writer; loop back and
                // keep using the same linked CTS — the deadline timer
                // continues running against the original wall-clock.
            }
        }

        if (!_channel.Writer.TryWrite(logEvent))
        {
            FireDrop(logEvent, DropReason.CapacityFull,
                "Async logger queue is full. The event was dropped.");
        }
    }

    private void FireDrop(LogEvent logEvent, DropReason reason, string diagnosticMessage)
    {
        _onEventDropped?.Invoke(logEvent);
        _onEventDroppedWithReason?.Invoke(logEvent, reason);

        _failureSink.ReportFailure(
            logEvent,
            new InvalidOperationException(diagnosticMessage),
            nameof(AsyncLogger));

        // Pipeline-level drop — no single sink owns it. Attribute to every
        // registered collector so the Prometheus snapshot reflects reality
        // (sum of per-sink drops = total pipeline drops for this decorator).
        // NullPipelineDropSink handles the unwired case without a branch.
        _dropSink.RecordDrop();

        // Mirror the drop on the rejected-event broadcaster so a dashboard
        // subscriber can render the dropped entry dimmed alongside accepted
        // ones. Single delegate-null branch when no subscriber is wired.
        Failures.RejectedEventBroadcaster.Publish(logEvent, DropReasons.QueueFull);
    }

    /// <summary>
    /// Async version of Log(). Awaits a slot when the queue is full (backpressure mode)
    /// without blocking the calling thread. Preferred over Log() in async code paths.
    /// With non-backpressure strategies, behaves identically to Log().
    ///
    /// <para>
    /// The signature matches <see cref="ILogger.LogAsync"/> exactly. An earlier
    /// build defined this overload without the <see cref="CancellationToken"/>
    /// parameter, which silently fell back to the default interface method
    /// (synchronous <see cref="Log"/>) for callers going through the
    /// <see cref="ILogger"/> contract — defeating the async backpressure path.
    /// The non-backpressure branch returns <see cref="ValueTask.CompletedTask"/>
    /// so the synchronous-completion fast path stays allocation-free.
    /// </para>
    /// </summary>
    public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_useBackpressure)
        {
            return WriteWithBackpressureAsync(logEvent, cancellationToken);
        }

        if (!_channel.Writer.TryWrite(logEvent))
        {
            FireDrop(logEvent, DropReason.CapacityFull,
                "Async logger queue is full. The event was dropped.");
        }

        return ValueTask.CompletedTask;
    }

    // Hoisted into a separate async method so the non-backpressure path above
    // stays a non-async ValueTask returner — that path takes the
    // ValueTask.CompletedTask fast path with zero state-machine allocation.
    private async ValueTask WriteWithBackpressureAsync(LogEvent logEvent, CancellationToken cancellationToken)
    {
        // Async-side timeout enforcement. The sync Log path already bounds
        // a stuck downstream sink via a deadline-based loop; the async path
        // used to await WriteAsync indefinitely, honouring only the caller's
        // token. A caller passing CancellationToken.None could therefore
        // hang on a sick sink forever — the exact hang the sync path was
        // written to prevent.
        //
        // Apply the same deadline: _waitTimeout linked to the caller's
        // token and the logger's own shutdown token. On timeout, drop the
        // event with SyncWaitTimeout and return — the caller keeps running.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            _cancellationTokenSource.Token, cancellationToken);
        linked.CancelAfter(_waitTimeout);

        try
        {
            await _channel.Writer.WriteAsync(logEvent, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            !_cancellationTokenSource.IsCancellationRequested)
        {
            // Neither the caller nor the logger asked to stop — the
            // cancellation came from our deadline. Treat as a drop.
            FireDrop(logEvent, DropReason.SyncWaitTimeout,
                "AsyncLogger async wait-timeout: downstream did not drain within the configured deadline.");
        }
    }

    public async ValueTask DisposeAsync() {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _channel.Writer.TryComplete();

        try
        {
            if (_drainTimeout.HasValue)
            {
                // Wait for drain with timeout - prevents shutdown hangs when sinks are slow
                var completedTask = await Task.WhenAny(
                    _processingTask,
                    Task.Delay(_drainTimeout.Value)).ConfigureAwait(false);

                if (completedTask != _processingTask)
                {
                    // Drain timed out - cancel and proceed
                    _cancellationTokenSource.Cancel();
                }
            }
            else
            {
                // Unbounded wait for drain (original behavior)
                await _processingTask.ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Shutdown should not fail loudly because of logging teardown.
        }
        finally
        {
            if (!_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
            }

            _cancellationTokenSource.Dispose();
        }
    }

    // Note: drop_write and drop_oldest strategies are handled by our Log() method
    // (TryWrite returns false -> we invoke callback + report to failure sink).
    // The channel itself always uses Wait mode so TryWrite reliably signals fullness.

    public string Describe() => $"AsyncLogger(capacity:{_channel.Reader.Count},drop:{(_useBackpressure ? Services.KnownDropStrategies.Wait : Services.KnownDropStrategies.DropWrite)})";

    // -- IComponentMetadata --
    internal static readonly Configuration.PipelineStepRules StepRules = new(
        PreferAfter: ["swappable"],
        RecommendPresent: ["rendering"]);
    string IComponentMetadata.ComponentName => "async";
    string IComponentMetadata.DisplayName => "Async Queue";
    string IComponentMetadata.Description => "Offloads log events to a background worker thread via bounded queue.";
    string IComponentMetadata.Help => "The Async Queue decouples the calling thread from log processing. Events are placed into a bounded channel and processed by a background worker. Configure capacity, drop strategy (drop_write or wait), and drain timeout.";
    VendorInfo IComponentMetadata.Vendor => VendorInfo.MMP;
    Configuration.PipelineStepRules IComponentMetadata.Rules => StepRules;
    internal static readonly System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> DefaultSchema =
    [
        Routing.SinkConfigField.Int("capacity", Services.PipelineDefaults.AsyncCapacity, "Queue capacity",
            "Maximum number of log events the bounded channel can hold before backpressure or dropping kicks in. Higher values buffer more during bursts but use more memory. Monitor queue depth in the Pipeline State panel to tune.", required: true),
        Routing.SinkConfigField.Choice("dropStrategy", Services.KnownDropStrategies.DropWrite, "Drop strategy",
            "What happens when the queue is full. 'drop_write' silently discards the newest event (non-blocking). 'wait' blocks the calling thread until space is available (guarantees delivery but can stall your application).",
            "drop_write", "wait"),
    ];
    System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> IComponentMetadata.ConfigurationSchema =>
    [
        DefaultSchema[0] with { DefaultValue = Capacity },
        DefaultSchema[1] with { DefaultValue = DropStrategy },
    ];
    private async Task ProcessQueueAsync(CancellationToken cancellationToken) {
        // Drain through LogAsync so network sinks that override it can use
        // HttpClient.SendAsync / Stream.WriteAsync end-to-end. Everything
        // else still flows through the default ILogger.LogAsync, which
        // calls Log synchronously — so the hot path for local sinks is
        // unchanged.
        try
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out var logEvent))
                {
                    try
                    {
                        await _next.LogAsync(logEvent, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Shutdown races — let the outer catch handle teardown.
                        throw;
                    }
                    catch (Exception exception)
                    {
                        _failureSink.ReportFailure(logEvent, exception, nameof(AsyncLogger));
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
