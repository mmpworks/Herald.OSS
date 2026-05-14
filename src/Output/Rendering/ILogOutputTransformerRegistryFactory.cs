#nullable enable

using MMP.Herald.Expansions;

namespace MMP.Herald.Output.Rendering;
/// <summary>
/// Creates a configured output transformer registry.
/// The factory receives the expansion registry because transformers may be wrapped
/// in expansion-aware decorators during registration.
/// </summary>
public interface ILogOutputTransformerRegistryFactory
{
    ILogOutputTransformerRegistry Create(
        ILogLevelOutputExpansionRegistry expansionRegistry);
}