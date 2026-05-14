#nullable enable

namespace MMP.Herald.Levels;
/// <summary>
/// Creates the default application log level ordering.
/// </summary>
public sealed class DefaultLogLevelRegistryFactory : ILogLevelRegistryFactory
{
    public ILogLevelRegistry Create()
    {
        var levelRegistry = LogLevelRegistry.CreateDefault();

        levelRegistry.RegisterAfter(KnownLogLevels.Info.Key, KnownLogLevels.Metric);
        levelRegistry.RegisterAfter(KnownLogLevels.Metric.Key, KnownLogLevels.Notice);
        levelRegistry.RegisterAfter(KnownLogLevels.Notice.Key, KnownLogLevels.Success);
        levelRegistry.RegisterAfter(KnownLogLevels.Error.Key, KnownLogLevels.Critical);
        levelRegistry.RegisterAfter(KnownLogLevels.Critical.Key, KnownLogLevels.Security);

        return levelRegistry;
    }
}