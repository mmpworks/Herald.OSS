#nullable enable

using System;
using System.Collections.Generic;

namespace MMP.Herald.Output.Rendering.Themes;

/// <summary>
/// Theme backed by dictionaries of element defaults and qualified overrides.
/// Resolution order: qualified override → element default → null.
/// </summary>
public sealed class ConfigurableConsoleTheme : IConsoleTheme
{
    private readonly Dictionary<string, OutputStyle> _elementDefaults;
    private readonly Dictionary<string, OutputStyle> _qualifiedOverrides;

    public ConfigurableConsoleTheme(
        IReadOnlyList<ThemeEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _elementDefaults = new Dictionary<string, OutputStyle>(StringComparer.OrdinalIgnoreCase);
        _qualifiedOverrides = new Dictionary<string, OutputStyle>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var elementKey = entry.Element.Key;

            if (entry.Qualifier is not null)
            {
                _qualifiedOverrides[$"{elementKey}:{entry.Qualifier}"] = entry.Style;
            }
            else
            {
                _elementDefaults[elementKey] = entry.Style;
            }
        }
    }

    public OutputStyle? Resolve(OutputElementKind element, string? qualifier = null)
    {
        var elementKey = element.Key;

        if (qualifier is not null &&
            _qualifiedOverrides.TryGetValue($"{elementKey}:{qualifier}", out var qualifiedStyle))
        {
            return qualifiedStyle;
        }

        return _elementDefaults.TryGetValue(elementKey, out var defaultStyle)
            ? defaultStyle
            : null;
    }

    /// <summary>
    /// A single theme entry binding an element kind + optional qualifier to a style.
    /// </summary>
    public sealed record ThemeEntry(
        OutputElementKind Element,
        OutputStyle Style,
        string? Qualifier = null);
}
