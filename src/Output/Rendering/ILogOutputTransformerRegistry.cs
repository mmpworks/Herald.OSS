#nullable enable

using MMP.Herald.Output.Aliases;

namespace MMP.Herald.Output.Rendering;
/// <summary>
/// Lookup for presentation transformers by alias.
/// </summary>
public interface ILogOutputTransformerRegistry
{
    void Register(LogOutputAlias alias, ILogOutputTransformer transformer);
    ILogOutputTransformer Get(LogOutputAlias alias);
}