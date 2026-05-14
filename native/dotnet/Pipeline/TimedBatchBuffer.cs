#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMP.Herald.Events;
using MMP.Herald.Failures;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Shared buffer that accumulates log events and flushes them by size, by time, or on dispose.
/// The flush behavior is determined by the callback provided at construction.
/// Used by BatchingLogger and PostFilteringBatchLogger to eliminate timer+buffer duplication.
/// </summary>
public sealed class TimedBatchBuffer : IAsyncDisposable
{
    private readonly Action<IReadOnlyList<LogEvent>> _flushCallback;
    private readonly ILogFailureSink _failureSink;
    private readonly string _callerName;
    private readonly int _maxBatchSize;
    private readonly List<LogEvent> _buffer;
    private readonly object _sync = new();
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Task _timerTask;
    private bool _isDisposed;

    public TimedBatchBuffer(
        Action<IReadOnlyList<LogEvent>> flushCallback,
        ILogFailureSink failureSink,
        string callerName,
        int maxBatchSize = Services.PipelineDefaults.BatchSize,
        int maxBatchDelayMs = Services.PipelineDefaults.BatchDelayMs)
    {
        if (maxBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBatchSize), maxBatchSize, "Max batch size must be greater than zero.");
        }

        if (maxBatchDelayMs <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBatchDelayMs), maxBatchDelayMs, "Max batch delay must be greater than zero.");
        }

        _flushCallback = flushCallback ?? throw new ArgumentNullException(nameof(flushCallback));
        _failureSink = failureSink ?? throw new ArgumentNullException(nameof(failureSink));
        _callerName = callerName ?? throw new ArgumentNullException(nameof(callerName));
        _maxBatchSize = maxBatchSize;
        _buffer = new List<LogEvent>(maxBatchSize);
        _cancellationTokenSource = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(maxBatchDelayMs));
        _timerTask = RunFlushLoopAsync(_cancellationTokenSource.Token);
    }

    public void Add(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        List<LogEvent>? batchToFlush = null;

        lock (_sync)
        {
            _buffer.Add(logEvent);

            if (_buffer.Count >= _maxBatchSize)
            {
                batchToFlush = DrainBufferUnsafe();
            }
        }

        if (batchToFlush is not null)
        {
            FlushBatch(batchToFlush);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _cancellationTokenSource.Cancel();

        try
        {
            await _timerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        List<LogEvent>? finalBatch;

        lock (_sync)
        {
            finalBatch = _buffer.Count == 0 ? null : DrainBufferUnsafe();
        }

        if (finalBatch is not null)
        {
            FlushBatch(finalBatch);
        }

        _timer.Dispose();
        _cancellationTokenSource.Dispose();
    }

    private async Task RunFlushLoopAsync(CancellationToken cancellationToken)
    {
        while (await _timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            List<LogEvent>? batchToFlush;

            lock (_sync)
            {
                batchToFlush = _buffer.Count == 0 ? null : DrainBufferUnsafe();
            }

            if (batchToFlush is not null)
            {
                FlushBatch(batchToFlush);
            }
        }
    }

    private List<LogEvent> DrainBufferUnsafe()
    {
        var batch = new List<LogEvent>(_buffer);
        _buffer.Clear();
        return batch;
    }

    private void FlushBatch(IReadOnlyList<LogEvent> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            _flushCallback(batch);
        }
        catch (Exception exception)
        {
            foreach (var logEvent in batch)
            {
                _failureSink.ReportFailure(logEvent, exception, _callerName);
            }
        }
    }
}
