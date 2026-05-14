#nullable enable

using System;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Output.Aliases;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Output.Rich;
using MMP.Herald.Pipeline;
using MMP.Herald.Pipeline.Kernel;

namespace MMP.Herald.Output.Writers;
/// <summary>
/// Logger adapter that renders an event through an alias transformer
/// and writes the final rendered output through a host adapter.
///
/// Implements <see cref="IKernelSink"/> so pipelines with a console sink
/// pass <see cref="KernelEligibility"/> and get a compiled kernel
/// delegate. The console render path still materialises a heap
/// <see cref="LogEvent"/> at the sink boundary today —
/// <see cref="LogRenderContext"/> and the
/// <see cref="ILogOutputTransformer"/> contract both take
/// <see cref="LogEvent"/> — so the allocation win is deferred until
/// those contracts grow a buffer overload. The time savings come from
/// the kernel compile: direct fan-out to sinks, no decorator chain walk.
/// </summary>
public sealed class RenderedOutputLogger : ILogger, IKernelSink
{
    private readonly LogOutputAlias _alias;
    private readonly ILogOutputTransformerRegistry _transformerRegistry;
    private readonly ILogLevelRegistry _levelRegistry;
    private readonly IRenderedLogOutputWriter _writer;

    public RenderedOutputLogger(
        LogOutputAlias alias,
        ILogOutputTransformerRegistry transformerRegistry,
        ILogLevelRegistry levelRegistry,
        IRenderedLogOutputWriter writer)
    {
        _alias = alias;
        _transformerRegistry = transformerRegistry;
        _levelRegistry = levelRegistry;
        _writer = writer;
    }

    public void Log(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var transformer = _transformerRegistry.Get(_alias);
        var context = new LogRenderContext(logEvent, _alias, _levelRegistry);
        var output = transformer.Transform(context);

        _writer.Write(output);
    }

    /// <summary>
    /// Kernel-path variant. Builds a stack-only
    /// <see cref="LogRenderBufferContext"/> and dispatches through the
    /// transformer's buffer overload. Transformers that override
    /// <see cref="ILogOutputTransformer.Transform(in LogRenderBufferContext)"/>
    /// read the buffer in place; transformers that don't materialise
    /// once via the default implementation. Either way, the pipeline
    /// stays kernel-eligible and skips the decorator chain.
    /// </summary>
    public void Log(in LogEventBuffer buffer)
    {
        var transformer = _transformerRegistry.Get(_alias);
        var context = new LogRenderBufferContext(buffer, _alias, _levelRegistry);
        var output = transformer.Transform(in context);

        _writer.Write(output);
    }
}
