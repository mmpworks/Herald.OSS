#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Pooling;
using MMP.Herald.Services;
using MMP.Herald.Templating;

namespace MMP.Herald.Formatting;

/// <summary>
/// NDJSON formatter for storage-oriented sinks.
/// Emits template, rendered message, properties, and context.
/// </summary>
public sealed class JsonFormatter : ILogFormatter
{
    private readonly ILogLevelRegistry _levelRegistry;

    public JsonFormatter(ILogLevelRegistry levelRegistry)
    {
        _levelRegistry = levelRegistry;
    }

    public string Format(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var registeredLevel = _levelRegistry.GetRegisteredLevel(logEvent.Level);
        var builder = StringBuilderPool.Rent();

        builder.Append('{');
        AppendStringProperty(builder, "time", logEvent.TimeUtc.ToString("O", CultureInfo.InvariantCulture));
        builder.Append(',');
        AppendStringProperty(builder, "level", registeredLevel.Level.DisplayName);
        builder.Append(',');
        AppendStringProperty(builder, Services.JsonOutputKeys.LevelKey, registeredLevel.Level.Key);
        builder.Append(',');
        AppendStringProperty(builder, Services.JsonOutputKeys.LevelRank, registeredLevel.Rank.ToString(CultureInfo.InvariantCulture));
        builder.Append(',');
        AppendStringProperty(builder, "category", logEvent.Category.Value);
        builder.Append(',');
        AppendStringProperty(builder, Services.JsonOutputKeys.MessageTemplate, logEvent.MessageTemplate);
        builder.Append(',');
        AppendStringProperty(builder, "message", logEvent.Message);
        builder.Append(',');
        AppendPropertiesObject(builder, "properties", logEvent.Properties);
        builder.Append(',');
        AppendContextObject(builder, "context", logEvent.Context);
        builder.Append('}');

        return StringBuilderPool.ReturnAndGetString(builder);
    }

    /// <summary>
    /// Kernel-path overload. Reads fields directly from the stack-allocated
    /// buffer and skips the per-event <see cref="LogEvent"/> + property-array
    /// allocations the legacy <see cref="Format(LogEvent)"/> path pays.
    /// Context is always empty on the kernel path (gated in
    /// <c>StructuredLogger.Log</c>), so context emission is skipped;
    /// properties come from whichever span the buffer carries (legacy or
    /// compact) — callers typically land on the compact span from the
    /// interpolated-string handlers.
    /// </summary>
    public string Format(in LogEventBuffer buffer)
    {
        var registeredLevel = _levelRegistry.GetRegisteredLevel(buffer.Level);
        var builder = StringBuilderPool.Rent();

        builder.Append('{');
        AppendStringProperty(builder, "time", buffer.TimeUtc.ToString("O", CultureInfo.InvariantCulture));
        builder.Append(',');
        AppendStringProperty(builder, "level", registeredLevel.Level.DisplayName);
        builder.Append(',');
        AppendStringProperty(builder, Services.JsonOutputKeys.LevelKey, registeredLevel.Level.Key);
        builder.Append(',');
        AppendStringProperty(builder, Services.JsonOutputKeys.LevelRank, registeredLevel.Rank.ToString(CultureInfo.InvariantCulture));
        builder.Append(',');
        AppendStringProperty(builder, "category", buffer.Category.Value);
        builder.Append(',');
        AppendStringProperty(builder, Services.JsonOutputKeys.MessageTemplate, buffer.MessageTemplate);
        builder.Append(',');
        AppendStringProperty(builder, "message", buffer.Message);
        builder.Append(',');

        // Property emission — prefer the compact span when populated (the
        // zero-alloc path from the interpolated-string handlers). Fall
        // back to the legacy span if the caller handed one in.
        if (!buffer.CompactProperties.IsEmpty)
        {
            AppendCompactPropertiesObject(builder, "properties", buffer.CompactProperties);
        }
        else if (!buffer.Properties.IsEmpty)
        {
            AppendPropertiesSpan(builder, "properties", buffer.Properties);
        }
        else
        {
            builder.Append("\"properties\":{}");
        }

        // Context is always empty on the kernel path — keep the field
        // present for wire compatibility with the legacy formatter but
        // skip the iteration.
        builder.Append(',');
        builder.Append("\"context\":{}");
        builder.Append('}');

        return StringBuilderPool.ReturnAndGetString(builder);
    }

    private static void AppendCompactPropertiesObject(
        StringBuilder builder,
        string name,
        ReadOnlySpan<LogPropertyCompact> properties)
    {
        builder.Append('"');
        builder.Append(Escape(name));
        builder.Append("\":{");

        for (var index = 0; index < properties.Length; index++)
        {
            var property = properties[index];

            builder.Append('"');
            builder.Append(Escape(property.Name));
            builder.Append("\":{");
            AppendStringProperty(builder, "value", property.Value?.ToString() ?? "null");
            builder.Append('}');

            if (index < properties.Length - 1)
            {
                builder.Append(',');
            }
        }

        builder.Append('}');
    }

    private static void AppendPropertiesSpan(
        StringBuilder builder,
        string name,
        ReadOnlySpan<LogProperty> properties)
    {
        builder.Append('"');
        builder.Append(Escape(name));
        builder.Append("\":{");

        for (var index = 0; index < properties.Length; index++)
        {
            var property = properties[index];

            builder.Append('"');
            builder.Append(Escape(property.Name));
            builder.Append("\":{");
            AppendStringProperty(builder, "value", property.ResolvedValue?.ToString() ?? "null");
            builder.Append(',');
            AppendStringProperty(builder, "capture_mode", property.CaptureModeOrDefault.Value);

            if (!string.IsNullOrWhiteSpace(property.Format))
            {
                builder.Append(',');
                AppendStringProperty(builder, "format", property.Format!);
            }

            builder.Append('}');

            if (index < properties.Length - 1)
            {
                builder.Append(',');
            }
        }

        builder.Append('}');
    }

    private static void AppendPropertiesObject(
        StringBuilder builder,
        string name,
        IReadOnlyList<LogProperty> properties)
    {
        builder.Append('"');
        builder.Append(Escape(name));
        builder.Append("\":{");

        var collapsedProperties = PropertyCollapser.Collapse(properties);

        for (var index = 0; index < collapsedProperties.Count; index += 1)
        {
            var property = collapsedProperties[index];

            builder.Append('"');
            builder.Append(Escape(property.Name));
            builder.Append("\":{");

            AppendStringProperty(builder, "value", property.ResolvedValue?.ToString() ?? "null");
            builder.Append(',');
            AppendStringProperty(builder, "capture_mode", property.CaptureModeOrDefault.Value);

            if (!string.IsNullOrWhiteSpace(property.Format))
            {
                builder.Append(',');
                AppendStringProperty(builder, "format", property.Format!);
            }

            builder.Append('}');

            if (index < collapsedProperties.Count - 1)
            {
                builder.Append(',');
            }
        }

        builder.Append('}');
    }


    private static void AppendContextObject(
        StringBuilder builder,
        string name,
        IReadOnlyDictionary<string, object?> context)
    {
        builder.Append('"');
        builder.Append(Escape(name));
        builder.Append("\":{");

        using var sorted = SortedContextBuffer.Create(context);

        for (var index = 0; index < sorted.Count; index += 1)
        {
            var pair = sorted[index];

            if (pair.Value is Exception exception)
            {
                AppendExceptionObject(builder, pair.Key, exception);
            }
            else
            {
                AppendStringProperty(builder, pair.Key, pair.Value?.ToString() ?? "null");
            }

            if (index < sorted.Count - 1)
            {
                builder.Append(',');
            }
        }

        builder.Append('}');
    }

    // Exception.Message and Exception.StackTrace are written verbatim. Upstream
    // code that throws with secret-bearing messages ("Invalid token: Bearer abc")
    // sees those strings land in logs. This matches every other .NET logger,
    // but it is worth naming explicitly: apply a LogOutputProcessor or scrub
    // secrets before they reach exception constructors. The ExceptionDetailEnricher
    // can also be replaced with one that filters known secret patterns upstream
    // of formatting.
    private static void AppendExceptionObject(StringBuilder builder, string name, Exception exception)
    {
        builder.Append('"');
        builder.Append(Escape(name));
        builder.Append("\":{");

        AppendStringProperty(builder, "type", exception.GetType().FullName ?? exception.GetType().Name);
        builder.Append(',');
        AppendStringProperty(builder, "message", exception.Message);
        builder.Append(',');
        AppendStringProperty(builder, Services.JsonOutputKeys.StackTrace, exception.StackTrace ?? string.Empty);

        if (exception.InnerException is not null)
        {
            builder.Append(",\"inner\":{");
            AppendStringProperty(builder, "type",
                exception.InnerException.GetType().FullName ?? exception.InnerException.GetType().Name);
            builder.Append(',');
            AppendStringProperty(builder, "message", exception.InnerException.Message);
            builder.Append(',');
            AppendStringProperty(builder, Services.JsonOutputKeys.StackTrace, exception.InnerException.StackTrace ?? string.Empty);
            builder.Append('}');
        }

        builder.Append('}');
    }

    private static void AppendStringProperty(
        StringBuilder builder,
        string name,
        string value)
    {
        builder.Append('"');
        builder.Append(Escape(name));
        builder.Append("\":\"");
        builder.Append(Escape(value));
        builder.Append('"');
    }

    private static string Escape(string value) => JsonEscaper.Escape(value);
}