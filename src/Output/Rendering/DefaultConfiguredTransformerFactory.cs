#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Output.Rendering.Themes;

namespace MMP.Herald.Output.Rendering;

/// <summary>
/// Default transformer factory for configured aliases.
/// Supported transformer kinds:
/// - standard
/// - console
/// </summary>
public sealed class DefaultConfiguredTransformerFactory : IConfiguredTransformerFactory
{
    private readonly IConsoleTheme _theme;

    public DefaultConfiguredTransformerFactory(IConsoleTheme theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    public ILogOutputTransformer Create(
        LoggingRuntimeAliasDefinition aliasDefinition,
        IReadOnlyDictionary<string, ILogOutputTransformer> existingTransformers,
        ILogLevelStyleResolver styleResolver)
    {
        ArgumentNullException.ThrowIfNull(aliasDefinition);
        ArgumentNullException.ThrowIfNull(existingTransformers);

        return aliasDefinition.TransformerKind.ToLowerInvariant() switch
        {
            "standard" => new StandardLogOutputTransformer(),
            "console" => CreateConsoleTransformer(aliasDefinition, existingTransformers),
            _ => throw new InvalidOperationException(
                $"Unsupported transformer kind '{aliasDefinition.TransformerKind}' for alias '{aliasDefinition.Key}'.")
        };
    }

    private ILogOutputTransformer CreateConsoleTransformer(
        LoggingRuntimeAliasDefinition aliasDefinition,
        IReadOnlyDictionary<string, ILogOutputTransformer> existingTransformers)
    {
        ILogOutputTransformer baseTransformer;

        if (!string.IsNullOrWhiteSpace(aliasDefinition.BasedOnAlias))
        {
            if (!existingTransformers.TryGetValue(aliasDefinition.BasedOnAlias, out baseTransformer!))
            {
                throw new KeyNotFoundException(
                    $"The base alias '{aliasDefinition.BasedOnAlias}' was not found for alias '{aliasDefinition.Key}'.");
            }
        }
        else
        {
            baseTransformer = new StandardLogOutputTransformer();
        }

        return new ConsoleOutputTransformer(baseTransformer, _theme);
    }
}
