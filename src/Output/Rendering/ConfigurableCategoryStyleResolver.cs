#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration.Runtime;

namespace MMP.Herald.Output.Rendering;

/// <summary>
/// Category style resolver backed by runtime configuration. Case-insensitive
/// category name lookup. Shape mirrors <see cref="ConfigurablePropertyStyleResolver"/>
/// so both resolvers have identical concurrency and lifetime contracts.
/// </summary>
public sealed class ConfigurableCategoryStyleResolver : ICategoryStyleResolver
{
    private readonly IReadOnlyDictionary<string, CategoryStyle> _styles;

    public ConfigurableCategoryStyleResolver(
        IReadOnlyList<LoggingRuntimeCategoryStyleDefinition> styleDefinitions)
    {
        ArgumentNullException.ThrowIfNull(styleDefinitions);

        var styles = new Dictionary<string, CategoryStyle>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in styleDefinitions)
        {
            styles[definition.CategoryName] = new CategoryStyle(
                definition.ColorName,
                definition.UseBold,
                definition.UseItalic,
                definition.BackgroundColorName);
        }

        _styles = styles;
    }

    public CategoryStyle? Resolve(string categoryName)
    {
        return _styles.TryGetValue(categoryName, out var style) ? style : null;
    }
}
