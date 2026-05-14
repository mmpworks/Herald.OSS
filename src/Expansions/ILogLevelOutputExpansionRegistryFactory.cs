#nullable enable

namespace MMP.Herald.Expansions;
/// <summary>
/// Creates a configured registry of per-level output expansions.
/// </summary>
public interface ILogLevelOutputExpansionRegistryFactory
{
    ILogLevelOutputExpansionRegistry Create();
}