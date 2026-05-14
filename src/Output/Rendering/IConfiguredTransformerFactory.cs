#nullable enable

using System.Collections.Generic;
using MMP.Herald.Configuration.Runtime;

namespace MMP.Herald.Output.Rendering;
/// <summary>
/// Creates a concrete transformer for a configured alias.
/// </summary>
public interface IConfiguredTransformerFactory
{
    ILogOutputTransformer Create(
        LoggingRuntimeAliasDefinition aliasDefinition,
        IReadOnlyDictionary<string, ILogOutputTransformer> existingTransformers,
        ILogLevelStyleResolver styleResolver);
}