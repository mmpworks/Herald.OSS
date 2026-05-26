#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MMP.Herald.Events;
using MMP.Herald.Failures;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Buffers events and flushes them to the next sink by size, by time, or on dispose.
/// Delegates buffer management to TimedBatchBuffer.
/// </summary>
public sealed class BatchingLogger : ILogger, IAsyncDisposable, IDescribable, IComponentMetadata
{
    private readonly ILogger _next;
    private readonly TimedBatchBuffer _buffer;
    private readonly int _maxBatchSize;
    private readonly int _maxBatchDelayMs;

    public BatchingLogger(
        ILogger next,
        ILogFailureSink? failureSink = null,
        int maxBatchSize = Services.PipelineDefaults.BatchSize,
        int maxBatchDelayMs = Services.PipelineDefaults.BatchDelayMs)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _maxBatchSize = maxBatchSize;
        _maxBatchDelayMs = maxBatchDelayMs;

        _buffer = new TimedBatchBuffer(
            FlushBatch,
            failureSink ?? NullLogFailureSink.Instance,
            nameof(BatchingLogger),
            maxBatchSize,
            maxBatchDelayMs);
    }

    public void Log(LogEvent logEvent)
    {
        _buffer.Add(logEvent);
    }

    public ValueTask DisposeAsync()
    {
        return _buffer.DisposeAsync();
    }

    private void FlushBatch(IReadOnlyList<LogEvent> batch)
    {
        if (_next is IBatchedLogSink batchedSink)
        {
            batchedSink.LogBatch(batch);
            return;
        }

        foreach (var logEvent in batch)
        {
            _next.Log(logEvent);
        }
    }
    public string Describe() => "BatchingLogger";

    // -- Inspection --

    /// <summary>The downstream logger batches are flushed to.</summary>
    public ILogger Inner => _next;

    // -- IComponentMetadata --
    internal static readonly Configuration.PipelineStepRules StepRules = new(
        PreferAfter: ["rendering"],
        PreferBefore: ["fanOut"]);
    string IComponentMetadata.ComponentName => "batching";
    string IComponentMetadata.DisplayName => "Batching";
    string IComponentMetadata.Description => "Groups events into batches for efficient sink delivery.";
    string IComponentMetadata.Help => "Accumulates events and flushes by size or time threshold. Reduces per-event overhead for network and database sinks.";
    VendorInfo IComponentMetadata.Vendor => VendorInfo.MMP;
    Configuration.PipelineStepRules IComponentMetadata.Rules => StepRules;

    // Default schema the dashboard renders when the batching step is in the strategy
    // but not yet instantiated. Live instances report their actual values below.
    internal static readonly System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> DefaultSchema =
    [
        Routing.SinkConfigField.Int("maxBatchSize", Services.PipelineDefaults.BatchSize, "Max batch size",
            "Flush the batch once this many events have accumulated. Larger batches reduce per-event sink overhead but add latency before delivery.", required: true),
        Routing.SinkConfigField.Int("maxBatchDelayMs", Services.PipelineDefaults.BatchDelayMs, "Max batch delay (ms)",
            "Flush the batch after this many milliseconds even if it has not reached the size threshold. Bounds worst-case delivery latency during low-volume periods.", required: true),
    ];

    System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> IComponentMetadata.ConfigurationSchema =>
    [
        DefaultSchema[0] with { DefaultValue = _maxBatchSize },
        DefaultSchema[1] with { DefaultValue = _maxBatchDelayMs },
    ];
}
