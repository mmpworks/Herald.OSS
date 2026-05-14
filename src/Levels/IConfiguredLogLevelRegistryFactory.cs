#nullable enable

using MMP.Herald.Configuration.Runtime;

namespace MMP.Herald.Levels;
/// <summary>
/// Creates a log level registry from runtime configuration.
/// </summary>
public interface IConfiguredLogLevelRegistryFactory
{
    ILogLevelRegistry Create(LoggingRuntimeLevelsConfiguration configuration);
}