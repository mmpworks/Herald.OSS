#nullable enable

using System;
using MMP.Herald.Events;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Forwards log events from one pipeline to another.
///
/// Use this to connect independent Herald pipelines in a hub-and-spoke pattern:
/// multiple specialized pipelines (gameplay, telemetry, compliance) each do their
/// own processing, then forward events to a shared pipeline (e.g., a console hub)
/// for unified output.
///
/// The bridge implements ILogger so it can be registered as a sink in any pipeline.
/// Events pass through without modification - the target pipeline applies its own
/// filtering, enrichment, and formatting independently.
///
/// Thread-safe: forwarding is a single method call on the target ILogger, which
/// is responsible for its own thread safety. The bridge holds no mutable state.
///
/// Usage:
///   // Build the console hub first
///   var consoleHub = QuickLogBuilder.Create()
///       .WithConsoleSink()
///       .WithMinimumLevel("info")
///       .Build();
///
///   // Bridge other pipelines into the hub
///   var bridge = new PipelineBridge(consoleHub.Logger);
///
///   // Gameplay pipeline: file + forward to console hub
///   var gameplay = QuickLogBuilder.Create()
///       .WithFileSink("logs/gameplay.log")
///       .WithCustomSinkProvider(new BridgeSinkProvider(bridge))
///       .WithMinimumLevel("debug")
///       .Build();
/// </summary>
public sealed class PipelineBridge : ILogger, IComponentMetadata
{
    private readonly ILogger _target;
    private readonly Func<LogEvent, bool>? _filter;

    /// <summary>
    /// Create a bridge that forwards all events to the target.
    /// </summary>
    /// <param name="target">The pipeline to forward events to.</param>
    public PipelineBridge(ILogger target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <summary>
    /// Create a bridge that forwards only events matching the filter.
    /// </summary>
    /// <param name="target">The pipeline to forward events to.</param>
    /// <param name="filter">Predicate that decides which events to forward. Return true to forward.</param>
    public PipelineBridge(ILogger target, Func<LogEvent, bool> filter)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
    }

    public void Log(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        if (_filter is not null && !_filter(logEvent))
            return;

        _target.Log(logEvent);
    }

    // -- IComponentMetadata --
    string IComponentMetadata.ComponentName => "pipelineBridge";
    string IComponentMetadata.DisplayName => "Pipeline Bridge";
    string IComponentMetadata.Description => "Forwards events from one pipeline to another.";
    string IComponentMetadata.Help => "Zero-state forwarder. Events are forwarded without re-parsing or re-enrichment. Optional filter predicate controls which events cross the bridge.";
    VendorInfo IComponentMetadata.Vendor => VendorInfo.MMP;
    System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> IComponentMetadata.ConfigurationSchema => [];
}
