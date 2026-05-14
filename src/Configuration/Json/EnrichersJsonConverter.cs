#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// Converter for <c>JsonLoggingConfig.Enrichers</c> that accepts both the
/// new <see cref="JsonEnricherConfig"/> object shape and the legacy bare
/// string-array shape. Without dual support, every saved
/// <c>pipeline-configs/*.json</c> from before the JSON-lossless refactor
/// would fail to deserialize at startup. The converter normalizes both
/// inputs to <see cref="JsonEnricherConfig"/> instances; legacy strings
/// land with <c>Properties = null</c>, matching the default
/// <see cref="Enrichers.ILogEnricher.ToJsonConfig"/> implementation.
///
/// <para>Write side always emits the new object form, so the next
/// <c>BuildAndCommit</c> cycle migrates the on-disk config forward.</para>
///
/// <para>
/// Properties values flow through the sibling
/// <see cref="ObjectDictionaryJsonConverter"/> (already registered on the
/// shared <see cref="JsonSerializerOptions"/>), so the AOT-unsafe
/// <c>JsonSerializer.Serialize&lt;TValue&gt;</c> overload is never reached.
/// </para>
/// </summary>
public sealed class EnrichersJsonConverter : JsonConverter<IReadOnlyList<JsonEnricherConfig>>
{
    public override IReadOnlyList<JsonEnricherConfig>? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected StartArray for enrichers, got {reader.TokenType}.");
        }

        var list = new List<JsonEnricherConfig>();
        var propsConverter = ResolvePropsConverter();

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.EndArray:
                    return list;

                case JsonTokenType.String:
                    // Legacy bare-string entry: kind only, no parameters.
                    var kind = reader.GetString();
                    if (string.IsNullOrWhiteSpace(kind))
                    {
                        throw new JsonException("Enricher kind string was empty.");
                    }
                    list.Add(new JsonEnricherConfig(kind));
                    break;

                case JsonTokenType.StartObject:
                    list.Add(ReadObject(ref reader, options, propsConverter));
                    break;

                default:
                    throw new JsonException(
                        $"Unexpected token {reader.TokenType} inside enrichers array; expected string or object.");
            }
        }

        throw new JsonException("Unterminated enrichers array.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        IReadOnlyList<JsonEnricherConfig> value,
        JsonSerializerOptions options)
    {
        var propsConverter = ResolvePropsConverter();

        writer.WriteStartArray();
        foreach (var entry in value)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", entry.Kind);
            if (entry.Properties is not null && entry.Properties.Count > 0)
            {
                writer.WritePropertyName("properties");
                propsConverter.Write(writer, entry.Properties, options);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    // Read a JsonEnricherConfig as a JSON object. Properties round-trip
    // through ObjectDictionaryJsonConverter so polymorphic primitive values
    // (string, bool, numeric, null, nested dicts/arrays) all parse without
    // touching the AOT-unsafe JsonSerializer.Deserialize<TValue> overload.
    private static JsonEnricherConfig ReadObject(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options,
        JsonConverter<IReadOnlyDictionary<string, object?>> propsConverter)
    {
        string? kind = null;
        IReadOnlyDictionary<string, object?>? properties = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                if (string.IsNullOrWhiteSpace(kind))
                {
                    throw new JsonException("Enricher object missing required 'kind' property.");
                }
                return new JsonEnricherConfig(kind, properties);
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Expected PropertyName, got {reader.TokenType}.");
            }

            var propertyName = reader.GetString();
            reader.Read();

            if (string.Equals(propertyName, "kind", StringComparison.OrdinalIgnoreCase))
            {
                kind = reader.GetString();
            }
            else if (string.Equals(propertyName, "properties", StringComparison.OrdinalIgnoreCase))
            {
                if (reader.TokenType == JsonTokenType.Null)
                {
                    properties = null;
                }
                else
                {
                    properties = propsConverter.Read(
                        ref reader, typeof(IReadOnlyDictionary<string, object?>), options);
                }
            }
            else
            {
                reader.Skip();
            }
        }

        throw new JsonException("Unterminated enricher object.");
    }

    private static JsonConverter<IReadOnlyDictionary<string, object?>> ResolvePropsConverter()
    {
        // The shared ObjectDictionaryJsonConverter singleton is the same
        // instance LoggingJsonSerializer registers on the source-gen
        // options, so primitive-shape rules and the error surface match
        // JsonPipelineStepConfig.Config exactly. Reaching for the field
        // directly (instead of options.GetConverter) keeps this call site
        // AOT/trim-clean — the GetConverter lookup is reflection-based.
        return ObjectDictionaryJsonConverter.Instance;
    }
}
