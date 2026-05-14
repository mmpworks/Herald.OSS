#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Expansions;
using MMP.Herald.Output.Aliases;

namespace MMP.Herald.Output.Rendering;
/// <summary>
/// Registers configured aliases and wraps them with expansion-aware transformers.
/// </summary>
public sealed class ConfiguredLogOutputTransformerRegistryFactory : ILogOutputTransformerRegistryFactory
{
    private readonly IReadOnlyList<LoggingRuntimeAliasDefinition> _aliasDefinitions;
    private readonly ILogLevelStyleResolver _styleResolver;
    private readonly IConfiguredTransformerFactory _transformerFactory;

    public ConfiguredLogOutputTransformerRegistryFactory(
        IReadOnlyList<LoggingRuntimeAliasDefinition> aliasDefinitions,
        ILogLevelStyleResolver styleResolver,
        IConfiguredTransformerFactory transformerFactory)
    {
        _aliasDefinitions = aliasDefinitions;
        _styleResolver = styleResolver;
        _transformerFactory = transformerFactory;
    }

    public ILogOutputTransformerRegistry Create(
        ILogLevelOutputExpansionRegistry expansionRegistry)
    {
        ArgumentNullException.ThrowIfNull(expansionRegistry);

        var registry = new LogOutputTransformerRegistry();
        var concreteTransformers = new Dictionary<string, ILogOutputTransformer>(StringComparer.OrdinalIgnoreCase);

        foreach (var aliasDefinition in _aliasDefinitions)
        {
            var concreteTransformer = _transformerFactory.Create(
                aliasDefinition,
                concreteTransformers,
                _styleResolver);

            concreteTransformers[aliasDefinition.Key] = concreteTransformer;

            var compositeTransformer = new CompositeLogOutputTransformer(
                concreteTransformer,
                expansionRegistry);

            registry.Register(
                new LogOutputAlias(aliasDefinition.Key),
                compositeTransformer);
        }

        return registry;
    }
}