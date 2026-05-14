#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rich;

namespace MMP.Herald.Output.Rendering;

/// <summary>
/// Renders the registered log levels as styled rich output using the configured
/// level style resolver so the dump matches runtime console styling.
/// </summary>
public sealed class ConfigurableLogLevelDumpRenderer
{
    private readonly ILogLevelStyleResolver _styleResolver;

    public ConfigurableLogLevelDumpRenderer(ILogLevelStyleResolver styleResolver)
    {
        _styleResolver = styleResolver;
    }

    public RenderedLogOutput Render(ILogLevelRegistry levelRegistry)
    {
        ArgumentNullException.ThrowIfNull(levelRegistry);

        var fragments = new List<RenderedLogFragment>
        {
            new RenderedLogFragment(
                Text: "Registered log levels",
                IsBold: true),

            new RenderedLogFragment("\n")
        };

        foreach (var registeredLevel in levelRegistry.GetRegisteredLevels())
        {
            var style = _styleResolver.Resolve(registeredLevel.Level);

            fragments.Add(new RenderedLogFragment(
                Text: registeredLevel.Rank.ToString(),
                ColorName: "dim_gray"));

            fragments.Add(new RenderedLogFragment(" - "));

            fragments.Add(new RenderedLogFragment(
                Text: registeredLevel.Level.DisplayName,
                ColorName: style.ColorName,
                IsBold: style.UseBold,
                IsItalic: style.UseItalic));

            fragments.Add(new RenderedLogFragment(" "));

            fragments.Add(new RenderedLogFragment(
                Text: "[",
                ColorName: "dim_gray"));

            fragments.Add(new RenderedLogFragment(
                Text: registeredLevel.Level.Key,
                ColorName: "dim_gray"));

            fragments.Add(new RenderedLogFragment(
                Text: "]",
                ColorName: "dim_gray"));

            fragments.Add(new RenderedLogFragment("\n"));
        }

        return new RenderedLogOutput(fragments);
    }
}