#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MMP.Herald.Events;
using MMP.Herald.Pooling;

namespace MMP.Herald.Templating;

/// <summary>
/// Renders parsed MessageTemplates into final message strings by substituting
/// property values. Extracted from MessageTemplateParser for single responsibility:
/// the parser turns strings into token trees, the renderer turns token trees into output.
///
/// Pooling strategy: all intermediate collections (property lookup, effective properties list,
/// seen set, string builder) are rented from thread-local pools and returned after use.
/// Only the final RenderedMessage and its contained string/list escape to the heap.
/// </summary>
public sealed class MessageTemplateRenderer
{
    private readonly DestructuringPolicyRegistry? _destructuringPolicies;

    public MessageTemplateRenderer(DestructuringPolicyRegistry? destructuringPolicies = null) {
        _destructuringPolicies = destructuringPolicies;
    }

    public RenderedMessage Render(
        MessageTemplate template,
        IReadOnlyList<LogProperty> properties) {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(properties);

        var propertyLookup = RentAndBuildPropertyLookup(properties);
        var effectiveProperties = BuildEffectiveProperties(template, propertyLookup, properties);
        var builder = StringBuilderPool.Rent();

        foreach (var token in template.Tokens)
        {
            switch (token)
            {
                case MessageTemplateToken.Text text:
                    builder.Append(text.Value);
                    break;

                case MessageTemplateToken.Property property:
                    AppendProperty(builder, property, propertyLookup);
                    break;
            }
        }

        CollectionPool.ReturnPropertyLookup(propertyLookup);

        return new RenderedMessage(
            Template: template.Raw,
            Message: StringBuilderPool.ReturnAndGetString(builder),
            Properties: effectiveProperties);
    }

    private void AppendProperty(
        StringBuilder builder,
        MessageTemplateToken.Property property,
        Dictionary<string, LogProperty> propertyLookup) {
        if (propertyLookup.TryGetValue(property.Name, out var logProperty))
        {
            if (!logProperty.IsSilent)
            {
                builder.Append(RenderPropertyValue(logProperty, property));
            }
        }
        else
        {
            AppendUnresolvedToken(builder, property);
        }
    }

    private static void AppendUnresolvedToken(
        StringBuilder builder, MessageTemplateToken.Property property) {
        builder.Append('{');

        if (property.CaptureMode == LogPropertyCaptureMode.Destructure)
            builder.Append('@');
        else if (property.CaptureMode == LogPropertyCaptureMode.Stringify)
            builder.Append('$');

        builder.Append(property.Name);

        if (!string.IsNullOrWhiteSpace(property.Format))
        {
            builder.Append(':');
            builder.Append(property.Format);
        }

        builder.Append('}');
    }

    /// <summary>
    /// Build the effective property list by merging original properties with template-declared
    /// properties. Uses a pooled list as scratch space and copies to a final array to minimize
    /// the escaping allocation (array is smaller than List with its internal buffer).
    /// </summary>
    private static IReadOnlyList<LogProperty> BuildEffectiveProperties(
        MessageTemplate template,
        IReadOnlyDictionary<string, LogProperty> propertyLookup,
        IReadOnlyList<LogProperty> originalProperties) {
        var scratch = CollectionPool.RentPropertyList();
        var seen = CollectionPool.RentStringSet();

        foreach (var property in originalProperties)
        {
            scratch.Add(property);
            seen.Add(property.Name);
        }

        // Track whether the second loop adds anything beyond the originals.
        // When nothing extra is added AND the original is already an array
        // (so it isn't a pooled list whose contents will be recycled), we
        // skip the scratch.ToArray() copy and return the user's array
        // directly — saves 16 + 40N bytes per accepted call on the common
        // case where the template only references properties the caller
        // already supplied with matching capture-mode / format.
        var addedExtra = false;

        foreach (var token in template.Tokens)
        {
            if (token is not MessageTemplateToken.Property propertyToken) continue;
            if (!propertyLookup.TryGetValue(propertyToken.Name, out var property)) continue;

            if (seen.Contains(propertyToken.Name) &&
                property.CaptureModeOrDefault == propertyToken.CaptureMode &&
                string.Equals(property.Format, propertyToken.Format, StringComparison.Ordinal))
            {
                continue;
            }

            seen.Add(propertyToken.Name);
            scratch.Add(new LogProperty(
                propertyToken.Name,
                property.ResolvedValue,
                propertyToken.CaptureMode,
                propertyToken.Format));
            addedExtra = true;
        }

        CollectionPool.ReturnStringSet(seen);

        IReadOnlyList<LogProperty> result;
        if (!addedExtra && originalProperties is LogProperty[])
        {
            // Caller-owned array, no template-only additions: hand it back.
            // The array is the user's, never a pooled list, so subsequent
            // CollectionPool returns can't corrupt the LogEvent.Properties
            // we just built.
            result = originalProperties;
        }
        else if (scratch.Count > 0)
        {
            // Either we added something, or the source isn't an array
            // (could be a pooled list from the standard factory path that
            // gets recycled after Render returns). Allocate a fresh array
            // at exact size — single allocation, no List slack.
            result = (IReadOnlyList<LogProperty>)scratch.ToArray();
        }
        else
        {
            result = LogEvent.EmptyProperties;
        }
        CollectionPool.ReturnPropertyList(scratch);
        return result;
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

    /// <summary>
    /// Render a property value to string. Avoids allocating a temporary LogProperty record
    /// by extracting the resolved value and applying capture mode / format directly.
    /// </summary>
    private string RenderPropertyValue(
        LogProperty suppliedProperty,
        MessageTemplateToken.Property propertyToken) {
        var value = suppliedProperty.ResolvedValue;

        if (value is null) return "null";

        var effectiveCaptureMode = propertyToken.CaptureMode ?? suppliedProperty.CaptureModeOrDefault;
        var effectiveFormat = propertyToken.Format ?? suppliedProperty.Format;

        if (effectiveCaptureMode == LogPropertyCaptureMode.Stringify)
            return value.ToString() ?? "null";

        if (!string.IsNullOrWhiteSpace(effectiveFormat) &&
            value is IFormattable formattable)
        {
            return formattable.ToString(effectiveFormat, CultureInfo.InvariantCulture)
                ?? "null";
        }

        if (effectiveCaptureMode == LogPropertyCaptureMode.Destructure)
            return RenderDestructuredValue(value);

        return value.ToString() ?? "null";
    }

    private string RenderDestructuredValue(object value) {
        if (_destructuringPolicies is not null &&
            _destructuringPolicies.TryDestructure(value, out var policyResult))
        {
            return policyResult ?? "null";
        }

        if (value is string stringValue) return stringValue;

        if (value is System.Collections.IEnumerable enumerable)
        {
            var parts = CollectionPool.RentStringList();
            foreach (var item in enumerable) parts.Add(item?.ToString() ?? "null");
            var result = $"[{string.Join(", ", parts)}]";
            CollectionPool.ReturnStringList(parts);
            return result;
        }

        return value.ToString() ?? "null";
    }
}
