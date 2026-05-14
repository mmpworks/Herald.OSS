#nullable enable

using MMP.Herald.Configuration;

namespace MMP.Herald.Pipeline;
/// <summary>
/// Produces the top-level pipeline policy for a configured app/runtime.
/// </summary>
public interface ILogPipelinePolicyFactory
{
    LogPipelinePolicy Create();
}