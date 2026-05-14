#nullable enable

using MMP.Herald.Output.Rich;

namespace MMP.Herald.Output.Rendering;

/// <summary>
/// Transforms rendered output as a pipeline step.
/// Processors run after the base transformer and can modify, enrich,
/// or replace the output. Each processor receives the output from the
/// previous step and the original render context.
///
/// Unlike transformers (which create output from scratch), processors
/// refine existing output. This enables composable pipelines inspired
/// by Python's structlog processor model.
///
/// Examples:
///   - Add timestamps or correlation IDs as fragments
///   - Mask PII values in rendered text
///   - Inject signals based on content analysis
///   - Strip styling for plain text sinks
///   - Add BBCode/HTML wrappers for rich hosts
/// </summary>
public interface ILogOutputProcessor
{
    RenderedLogOutput Process(RenderedLogOutput output, LogRenderContext context);
}
