#nullable enable

using MMP.Herald.Levels;
using MMP.Herald.Output.Aliases;

namespace MMP.Herald.Expansions;
/// <summary>
/// Default core expansion registration.
/// Core stays engine-agnostic, so it only registers standard output expansions.
/// </summary>
public sealed class DefaultLogLevelOutputExpansionRegistryFactory : ILogLevelOutputExpansionRegistryFactory
{
    public ILogLevelOutputExpansionRegistry Create()
    {
        var registry = new LogLevelOutputExpansionRegistry();

        registry.Register(
            KnownLogLevels.Error,
            KnownLogOutputAliases.Standard,
            new StandardStackTraceExpansion());

        registry.Register(
            KnownLogLevels.Fatal,
            KnownLogOutputAliases.Standard,
            new StandardStackTraceExpansion());

        return registry;
    }
}