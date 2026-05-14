#nullable enable

using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;

namespace MMP.Herald.Routing;
/// <summary>
/// Creates concrete sinks from runtime sink definitions.
/// </summary>
public interface ILogSinkFactory
{
    ILogger Create(
        LoggingRuntimeSinkDefinition definition,
        ILogLevelRegistry levelRegistry,
        ILogOutputTransformerRegistry transformerRegistry);
}