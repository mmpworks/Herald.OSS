#nullable enable

using MMP.Herald.Configuration.Json;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Levels;

namespace MMP.Herald.Configuration;
/// <summary>
/// Maps JSON-facing config into runtime logging configuration.
/// </summary>
public interface ILoggingConfigurationMapper
{
    LoggingRuntimeConfiguration Map(JsonLoggingConfig jsonConfig, ILogLevelRegistry levelRegistry);
}