#nullable enable

using MMP.Herald.Configuration;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Time;

namespace MMP.Herald.Pipeline;
/// <summary>
/// Builds the top-level logger pipeline from routed sinks and policy.
/// </summary>
public interface ILogPipelineFactory
{
    LoggerComposition Create(
        ILogger routedSinks,
        IDateTimeProvider dateTimeProvider,
        ILogLevelRegistry levelRegistry,
        ILogFailureSink failureSink,
        LogPipelinePolicy policy);
}