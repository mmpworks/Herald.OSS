#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using MMP.Herald.Output.Rendering.Themes;
using MMP.Herald.Output.Rich;
using MMP.Herald.Pooling;
using MMP.Herald.Templating;

namespace MMP.Herald.Output.Rendering;

/// <summary>
/// Rich console transformer that uses an IConsoleTheme for all styling decisions.
/// The theme resolves styles for every output element: timestamp, level, category,
/// message text, property names/values, separators, and punctuation.
/// </summary>
public sealed class ConsoleOutputTransformer : ILogOutputTransformer
{
    private readonly ILogOutputTransformer _baseTransformer;
    private readonly IConsoleTheme _theme;
    private readonly MessageTemplateParser _templateParser;
    private readonly ConcurrentDictionary<string, MessageTemplate> _templateCache = new(StringComparer.Ordinal);
    private const int MaxCacheSize = 256;

    public ConsoleOutputTransformer(
        ILogOutputTransformer baseTransformer,
        IConsoleTheme theme)
    {
        _baseTransformer = baseTransformer;
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _templateParser = new MessageTemplateParser();
    }

    public RenderedLogOutput Transform(LogRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var registeredLevel = context.LevelRegistry.GetRegisteredLevel(context.Event.Level);
        var levelKey = context.Event.Level.Key;

        var levelStyle = _theme.Resolve(OutputElementKind.LevelText.Instance, levelKey);
        var timestampStyle = _theme.Resolve(OutputElementKind.Timestamp.Instance);
        // Pass the category name as the qualifier so per-category overrides
        // (registered via JsonLoggingConfig.CategoryStyles) win over the
        // default Category entry while leaving unknown categories on the fallback.
        var categoryStyle = _theme.Resolve(OutputElementKind.Category.Instance, context.Event.Category.Value);
        var separatorStyle = _theme.Resolve(OutputElementKind.Separator.Instance);

        var fragments = new List<RenderedLogFragment>
        {
            StyledFragment($"{registeredLevel.Level.DisplayName}:{registeredLevel.Rank}", levelStyle),
            StyledFragment(" ", separatorStyle),
            StyledFragment(context.Event.TimeUtc.ToString("O"), timestampStyle),
            StyledFragment(" ", separatorStyle),
            StyledFragment(context.Event.Category.Value, categoryStyle),
            StyledFragment(" - ", separatorStyle)
        };

        AppendMessageFragments(fragments, context);
        AppendOrderedContextFragments(fragments, context);

        return new RenderedLogOutput(fragments);
    }

    private void AppendMessageFragments(
        ICollection<RenderedLogFragment> fragments,
        LogRenderContext context)
    {
        var parsedTemplate = GetOrParseTemplate(context.Event.MessageTemplate);
        var propertyLookup = RentAndBuildPropertyLookup(context.Event.Properties);

        foreach (var token in parsedTemplate.Tokens)
        {
            switch (token)
            {
                case MessageTemplateToken.Text text:
                    var textStyle = _theme.Resolve(OutputElementKind.MessageText.Instance);
                    fragments.Add(StyledFragment(text.Value, textStyle));
                    break;

                case MessageTemplateToken.Property property:
                    // Silent properties are not rendered into message text.
                    // They are still available in structured/context output.
                    if (IsSilentProperty(property.Name, propertyLookup))
                    {
                        break;
                    }

                    var renderedValue = ResolveRenderedValue(property, propertyLookup);
                    var propStyle = _theme.Resolve(OutputElementKind.PropertyValue.Instance, property.Name);
                    fragments.Add(StyledFragment(renderedValue, propStyle));
                    break;
            }
        }

        CollectionPool.ReturnPropertyLookup(propertyLookup);
    }

    private static bool IsSilentProperty(
        string propertyName,
        IReadOnlyDictionary<string, LogProperty> propertyLookup)
    {
        return propertyLookup.TryGetValue(propertyName, out var prop) && prop.IsSilent;
    }

    private static string ResolveRenderedValue(
        MessageTemplateToken.Property propertyToken,
        IReadOnlyDictionary<string, LogProperty> propertyLookup)
    {
        if (!propertyLookup.TryGetValue(propertyToken.Name, out var logProperty))
        {
            return propertyToken.RawText;
        }

        var value = logProperty.ResolvedValue;

        if (value is null)
        {
            return "null";
        }

        if (propertyToken.CaptureMode == LogPropertyCaptureMode.Stringify)
        {
            return value.ToString() ?? "null";
        }

        if (!string.IsNullOrWhiteSpace(propertyToken.Format) && value is IFormattable formattable)
        {
            return formattable.ToString(propertyToken.Format, CultureInfo.InvariantCulture) ?? "null";
        }

        return value.ToString() ?? "null";
    }

    private static Dictionary<string, LogProperty> RentAndBuildPropertyLookup(
        IReadOnlyList<LogProperty> properties) {
        var lookup = CollectionPool.RentPropertyLookup();

        foreach (var property in properties)
        {
            lookup[property.Name] = property;
        }

        return lookup;
    }

    private void AppendOrderedContextFragments(
        ICollection<RenderedLogFragment> fragments,
        LogRenderContext context)
    {
        var punctuationStyle = _theme.Resolve(OutputElementKind.Punctuation.Instance);

        var sortedPairs = CollectionPool.RentContextPairs();
        foreach (var pair in context.Event.Context)
        {
            sortedPairs.Add(pair);
        }

        sortedPairs.Sort(static (a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal));

        foreach (var pair in sortedPairs)
        {
            var nameStyle = _theme.Resolve(OutputElementKind.PropertyName.Instance, pair.Key);
            var valueStyle = _theme.Resolve(OutputElementKind.PropertyValue.Instance, pair.Key);

            fragments.Add(StyledFragment(" ", null));
            fragments.Add(StyledFragment(pair.Key, nameStyle));
            fragments.Add(StyledFragment("=", punctuationStyle));
            fragments.Add(StyledFragment(pair.Value?.ToString() ?? "null", valueStyle));
        }

        CollectionPool.ReturnContextPairs(sortedPairs);
    }

    private MessageTemplate GetOrParseTemplate(string templateString)
    {
        if (_templateCache.Count > MaxCacheSize)
        {
            _templateCache.Clear();
        }

        return _templateCache.GetOrAdd(templateString, _templateParser.Parse);
    }

    private static RenderedLogFragment StyledFragment(string text, OutputStyle? style)
    {
        return style is not null
            ? new RenderedLogFragment(text, style.ColorName, style.UseBold, style.UseItalic, style.BackgroundColorName)
            : new RenderedLogFragment(text);
    }
}
