#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// AOT-safe converter for <see cref="IReadOnlyDictionary{String, Object}"/>.
/// System.Text.Json's source generator cannot emit metadata for <c>object</c>
/// values because the concrete type is not statically known, so the
/// source-gen context hands off to this converter for any property typed
/// as a dictionary of objects.
///
/// <para>
/// Scope: the converter handles the primitive shapes that actually show
/// up in <see cref="JsonPipelineStepConfig.Config"/> values — string,
/// bool, numeric, null, array of the same, and nested dictionaries.
/// Anything exotic (user-defined types stuffed into the config) throws
/// a clear error at serialization time rather than silently losing data.
/// </para>
///
/// <para><b>Thread safety.</b> Converter instance is stateless; the
/// source-gen context shares a single instance across every call.</para>
/// </summary>
public sealed class ObjectDictionaryJsonConverter : JsonConverter<IReadOnlyDictionary<string, object?>>
{
    /// <summary>
    /// Shared singleton. The converter is stateless, so callers that need
    /// to dispatch directly (e.g. EnrichersJsonConverter) reach for this
    /// field instead of asking <see cref="JsonSerializerOptions.GetConverter"/>
    /// — that lookup is reflection-based and trips IL2026 / IL3050.
    /// </summary>
    public static readonly ObjectDictionaryJsonConverter Instance = new();

    public override IReadOnlyDictionary<string, object?>? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StartObject, got {reader.TokenType}");
        }

        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return dict;

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Expected PropertyName, got {reader.TokenType}");
            }

            var key = reader.GetString()!;
            reader.Read();
            dict[key] = ReadValue(ref reader);
        }

        throw new JsonException("Unexpected end of JSON input while reading dictionary.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        IReadOnlyDictionary<string, object?> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (k, v) in value)
        {
            writer.WritePropertyName(k);
            WriteValue(writer, v);
        }
        writer.WriteEndObject();
    }

    private static object? ReadValue(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.Null => null,
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.Number => reader.TryGetInt64(out var l) ? l : reader.GetDouble(),
        JsonTokenType.StartArray => ReadArray(ref reader),
        JsonTokenType.StartObject => ReadNestedObject(ref reader),
        _ => throw new JsonException($"Unexpected token {reader.TokenType} while reading config value"),
    };

    private static List<object?> ReadArray(ref Utf8JsonReader reader)
    {
        var list = new List<object?>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) return list;
            list.Add(ReadValue(ref reader));
        }
        throw new JsonException("Unexpected end of JSON input while reading array.");
    }

    private static Dictionary<string, object?> ReadNestedObject(ref Utf8JsonReader reader)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return dict;
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Expected PropertyName, got {reader.TokenType}");
            }
            var key = reader.GetString()!;
            reader.Read();
            dict[key] = ReadValue(ref reader);
        }
        throw new JsonException("Unexpected end of JSON input while reading nested object.");
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); return;
            case bool b: writer.WriteBooleanValue(b); return;
            case string s: writer.WriteStringValue(s); return;
            case int i: writer.WriteNumberValue(i); return;
            case long l: writer.WriteNumberValue(l); return;
            case short sh: writer.WriteNumberValue(sh); return;
            case byte by: writer.WriteNumberValue(by); return;
            case uint ui: writer.WriteNumberValue(ui); return;
            case ulong ul: writer.WriteNumberValue(ul); return;
            case float f: writer.WriteNumberValue(f); return;
            case double d: writer.WriteNumberValue(d); return;
            case decimal dec: writer.WriteNumberValue(dec); return;
            case DateTime dt: writer.WriteStringValue(dt); return;
            case DateTimeOffset dto: writer.WriteStringValue(dto); return;
            case Guid g: writer.WriteStringValue(g); return;
            case IReadOnlyDictionary<string, object?> nested: WriteNested(writer, nested); return;
            case IEnumerable<object?> arr: WriteArray(writer, arr); return;
            default:
                throw new JsonException(
                    $"ObjectDictionaryJsonConverter: unsupported value type '{value.GetType().FullName}'. " +
                    "Only primitives, strings, arrays, and nested dictionaries are supported in Config values.");
        }
    }

    private static void WriteNested(Utf8JsonWriter writer, IReadOnlyDictionary<string, object?> nested)
    {
        writer.WriteStartObject();
        foreach (var (k, v) in nested)
        {
            writer.WritePropertyName(k);
            WriteValue(writer, v);
        }
        writer.WriteEndObject();
    }

    private static void WriteArray(Utf8JsonWriter writer, IEnumerable<object?> items)
    {
        writer.WriteStartArray();
        foreach (var item in items) WriteValue(writer, item);
        writer.WriteEndArray();
    }
}
