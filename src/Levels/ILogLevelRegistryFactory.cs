#nullable enable

namespace MMP.Herald.Levels;
/// <summary>
/// Creates a configured log level registry.
/// </summary>
public interface ILogLevelRegistryFactory
{
    ILogLevelRegistry Create();
}