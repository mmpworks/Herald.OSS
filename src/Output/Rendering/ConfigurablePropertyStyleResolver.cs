#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration.Runtime;

namespace MMP.Herald.Output.Rendering;

/// <summary>
/// Property style resolver backed by runtime configuration.
/// Case-insensitive property name lookup.
/// </summary>
public sealed class ConfigurablePropertyStyleResolver : IPropertyStyleResolver
{
    private readonly IReadOnlyDictionary<string, PropertyStyle> _styles;

    public ConfigurablePropertyStyleResolver(
        IReadOnlyList<LoggingRuntimePropertyStyleDefinition> styleDefinitions)
    {
        ArgumentNullException.ThrowIfNull(styleDefinitions);

        var styles = new Dictionary<string, PropertyStyle>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in styleDefinitions)
        {
            styles[definition.PropertyName] = new PropertyStyle(
                definition.ColorName,
                definition.UseBold,
                definition.UseItalic);
        }

        _styles = styles;
    }

    public PropertyStyle? Resolve(string propertyName)
    {
        return _styles.TryGetValue(propertyName, out var style) ? style : null;
    }
}
