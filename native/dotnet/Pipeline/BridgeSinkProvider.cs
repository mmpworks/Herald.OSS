#nullable enable

using System;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Routing;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Sink provider that registers a <see cref="PipelineBridge"/> as a sink.
/// Use with <c>WithCustomSinkProvider</c> on the QuickLogBuilder to route
/// events from one pipeline into another.
///
/// Usage:
///   var bridge = new PipelineBridge(targetLogger);
///   var source = QuickLogBuilder.Create()
///       .WithCustomSinkProvider(new BridgeSinkProvider(bridge))
///       .Build();
/// </summary>
public sealed class BridgeSinkProvider : ILogSinkProvider
{
    private readonly PipelineBridge _bridge;

    public BridgeSinkProvider(PipelineBridge bridge, string? sinkKind = null)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        SinkKind = sinkKind ?? Services.KnownSinkKinds.PipelineBridge;
    }

    public string SinkKind { get; }

    public ILogger CreateSink(
        LoggingRuntimeSinkDefinition definition,
        ILogLevelRegistry levelRegistry,
        ILogOutputTransformerRegistry transformerRegistry) => _bridge;
}
