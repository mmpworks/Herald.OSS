#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MMP.Herald.Events;
using MMP.Herald.Failures;
using MMP.Herald.Filters;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Buffers log events and applies an IBatchLogFilter during flush to decide which events
/// survive. Uses TimedBatchBuffer for buffer management.
/// </summary>
public sealed class PostFilteringBatchLogger : ILogger, IAsyncDisposable, IComponentMetadata
{
    private readonly ILogger _next;
    private readonly IBatchLogFilter _batchFilter;
    private readonly TimedBatchBuffer _buffer;

    public PostFilteringBatchLogger(
        ILogger next,
        IBatchLogFilter batchFilter,
        ILogFailureSink? failureSink = null,
        int maxBatchSize = Services.PipelineDefaults.BatchSize,
        int maxBatchDelayMs = Services.PipelineDefaults.BatchDelayMs)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _batchFilter = batchFilter ?? throw new ArgumentNullException(nameof(batchFilter));

        _buffer = new TimedBatchBuffer(
            FlushBatch,
            failureSink ?? NullLogFailureSink.Instance,
            nameof(PostFilteringBatchLogger),
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
        var survivors = _batchFilter.FilterBatch(batch);

        foreach (var logEvent in survivors)
        {
            _next.Log(logEvent);
        }
    }

    // -- IComponentMetadata (single source of truth) --
    internal static readonly Configuration.PipelineStepRules StepRules = new(
        PreferAfter: ["rendering"],
        PreferBefore: ["fanOut"]);

    string IComponentMetadata.ComponentName => "postFiltering";
    string IComponentMetadata.DisplayName => "Post-Filtering";
    string IComponentMetadata.Description => "Secondary filtering after rendering, e.g. content-based filters.";
    string IComponentMetadata.Help => "Applies IBatchLogFilter during flush. Can filter based on rendered message content. Place between Rendering and FanOut.";
    VendorInfo IComponentMetadata.Vendor => VendorInfo.MMP;
    Configuration.PipelineStepRules IComponentMetadata.Rules => StepRules;
    System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> IComponentMetadata.ConfigurationSchema => [];
}
