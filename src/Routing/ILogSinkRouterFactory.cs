#nullable enable

using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;

namespace MMP.Herald.Routing;
/// <summary>
/// Creates routed sinks from runtime configuration.
/// </summary>
public interface ILogSinkRouterFactory
{
    ILogger Create(
        LoggingRuntimeConfiguration runtimeConfiguration,
        ILogLevelRegistry levelRegistry,
        ILogOutputTransformerRegistry transformerRegistry);
}