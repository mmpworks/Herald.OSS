#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Levels;

namespace MMP.Herald.Output.Rendering;
/// <summary>
/// Style resolver backed by runtime configuration.
/// Unknown levels fall back to a neutral style.
/// </summary>
public sealed class ConfigurableLogLevelStyleResolver : ILogLevelStyleResolver
{
    private readonly IReadOnlyDictionary<string, LogLevelStyle> _stylesByLevelKey;

    public ConfigurableLogLevelStyleResolver(
        IReadOnlyList<LoggingRuntimeLevelStyleDefinition> styleDefinitions)
    {
        ArgumentNullException.ThrowIfNull(styleDefinitions);

        var styles = new Dictionary<string, LogLevelStyle>(StringComparer.OrdinalIgnoreCase);

        foreach (var styleDefinition in styleDefinitions)
        {
            styles[styleDefinition.LevelKey] = new LogLevelStyle(
                ColorName: styleDefinition.ColorName,
                UseBold: styleDefinition.UseBold,
                UseItalic: styleDefinition.UseItalic);
        }

        _stylesByLevelKey = styles;
    }

    public LogLevelStyle Resolve(LogLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);

        if (_stylesByLevelKey.TryGetValue(level.Key, out var style))
        {
            return style;
        }

        return new LogLevelStyle("white");
    }
}