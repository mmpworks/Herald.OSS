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

    public BatchingLogger(
        ILogger next,
        ILogFailureSink? failureSink = null,
        int maxBatchSize = Services.PipelineDefaults.BatchSize,
        int maxBatchDelayMs = Services.PipelineDefaults.BatchDelayMs)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));

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
    System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> IComponentMetadata.ConfigurationSchema => [];
}
