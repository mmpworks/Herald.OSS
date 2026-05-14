#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Output.Rich;

namespace MMP.Herald.Output.Rendering;

/// <summary>
/// Chains a base transformer with a sequence of output processors.
/// The base transformer creates the initial RenderedLogOutput, then
/// each processor refines it in order.
///
/// This is the structlog-style composable pipeline for .NET:
///   event -> base transform -> add_timestamp -> mask_pii -> inject_signals -> output
///
/// Usage:
///   var transformer = new PipelinedTransformer(
///       baseTransformer: new StandardLogOutputTransformer(),
///       processors:
///       [
///           new TimestampPrefixProcessor(),
///           new PiiMaskingProcessor(),
///           new SignalInjectionProcessor()
///       ]);
/// </summary>
public sealed class PipelinedTransformer : ILogOutputTransformer
{
    private readonly ILogOutputTransformer _base;
    private readonly IReadOnlyList<ILogOutputProcessor> _processors;

    public PipelinedTransformer(
        ILogOutputTransformer baseTransformer,
        IReadOnlyList<ILogOutputProcessor> processors)
    {
        _base = baseTransformer ?? throw new ArgumentNullException(nameof(baseTransformer));
        _processors = processors ?? throw new ArgumentNullException(nameof(processors));
    }

    public RenderedLogOutput Transform(LogRenderContext context)
    {
        var output = _base.Transform(context);

        foreach (var processor in _processors)
        {
            output = processor.Process(output, context);
        }

        return output;
    }
}
