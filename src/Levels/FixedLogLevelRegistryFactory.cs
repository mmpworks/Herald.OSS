#nullable enable

namespace MMP.Herald.Levels;
/// <summary>
/// Adapter that returns a prebuilt log level registry.
/// Useful when the registry was created from JSON or another external source.
/// </summary>
public sealed class FixedLogLevelRegistryFactory : ILogLevelRegistryFactory
{
    private readonly ILogLevelRegistry _levelRegistry;

    public FixedLogLevelRegistryFactory(ILogLevelRegistry levelRegistry)
    {
        _levelRegistry = levelRegistry;
    }

    public ILogLevelRegistry Create()
    {
        return _levelRegistry;
    }
}