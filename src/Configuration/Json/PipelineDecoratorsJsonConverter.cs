#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// Converter for <c>JsonLoggingConfig.PipelineDecorators</c>. Mirrors
/// <see cref="EnrichersJsonConverter"/>: accepts both the new object
/// shape and a legacy bare-string-array shape so configs hand-authored
/// or written by an earlier tool keep loading.
///
/// <para>
/// There is no pre-refactor on-disk shape to preserve here (custom
/// decorators were never serialized) — the dual-shape support is for
/// hand-authored configs that want to attach a stateless decorator with
/// a simple <c>"pipelineDecorators": ["myDecorator"]</c> form. The write
/// side always emits the object shape so any subsequent <c>BuildAndCommit</c>
/// migrates the on-disk config forward.
/// </para>
///
/// <para>
/// Properties values flow through the shared
/// <see cref="ObjectDictionaryJsonConverter"/> singleton so polymorphic
/// primitive values stay AOT-clean — no <c>JsonSerializer.Serialize&lt;TValue&gt;</c>
/// reflection path.
/// </para>
/// </summary>
public sealed class PipelineDecoratorsJsonConverter : JsonConverter<IReadOnlyList<JsonPipelineDecoratorConfig>>
{
    public override IReadOnlyList<JsonPipelineDecoratorConfig>? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected StartArray for pipelineDecorators, got {reader.TokenType}.");
        }

        var list = new List<JsonPipelineDecoratorConfig>();
        var propsConverter = ObjectDictionaryJsonConverter.Instance;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.EndArray:
                    return list;

                case JsonTokenType.String:
                    var kind = reader.GetString();
                    if (string.IsNullOrWhiteSpace(kind))
                    {
                        throw new JsonException("Pipeline decorator kind string was empty.");
                    }
                    list.Add(new JsonPipelineDecoratorConfig(kind));
                    break;

                case JsonTokenType.StartObject:
                    list.Add(ReadObject(ref reader, options, propsConverter));
                    break;

                default:
                    throw new JsonException(
                        $"Unexpected token {reader.TokenType} inside pipelineDecorators array; expected string or object.");
            }
        }

        throw new JsonException("Unterminated pipelineDecorators array.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        IReadOnlyList<JsonPipelineDecoratorConfig> value,
        JsonSerializerOptions options)
    {
        var propsConverter = ObjectDictionaryJsonConverter.Instance;

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

    private static JsonPipelineDecoratorConfig ReadObject(
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
                    throw new JsonException("Pipeline decorator object missing required 'kind' property.");
                }
                return new JsonPipelineDecoratorConfig(kind, properties);
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

        throw new JsonException("Unterminated pipeline decorator object.");
    }
}
