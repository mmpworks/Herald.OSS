#nullable enable

using MMP.Herald.Output.Rich;

namespace MMP.Herald.Expansions;
/// <summary>
/// Post-transform hook that can rewrite or extend a rendered output.
/// </summary>
public interface ILogLevelOutputExpansion
{
    RenderedLogOutput Transform(LogRenderContext context, RenderedLogOutput current);
}