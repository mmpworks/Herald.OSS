#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.Json;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Services;
using MMP.Herald.Templating;

namespace MMP.Herald.Formatting;

/// <summary>
/// Zero-intermediate-string JSON formatter that writes directly to IBufferWriter&lt;byte&gt;
/// using System.Text.Json.Utf8JsonWriter. Eliminates StringBuilder, string encoding,
/// and the JSON escape chain that allocates up to 5 intermediate strings per value.
///
/// Pre-caches JsonEncodedText for known property names to avoid repeated encoding.
///
/// Thread safety: the Utf8JsonWriter is pooled per-thread via [ThreadStatic] and
/// rebound to the caller-supplied buffer on each Format call via Reset(output).
/// This drops the per-call writer allocation (~100 B) without taking a lock.
/// JsonEncodedText values are immutable and safe for concurrent reads.
/// </summary>
public sealed class Utf8JsonFormatter : IUtf8LogFormatter
{
    private readonly ILogLevelRegistry _levelRegistry;

    // [ThreadStatic] writer; not safe for recursive Format calls on the same thread
    // — pipeline contract is non-recursive.
    [ThreadStatic]
    private static Utf8JsonWriter? s_writer;

    private static readonly JsonWriterOptions s_writerOptions = new()
    {
        SkipValidation = true // Trust our structure for performance
    };

    // Pre-encoded property names - allocated once, reused forever
    private static readonly JsonEncodedText PropTime = JsonEncodedText.Encode("time");
    private static readonly JsonEncodedText PropLevel = JsonEncodedText.Encode("level");
    private static readonly JsonEncodedText PropLevelKey = JsonEncodedText.Encode(Services.JsonOutputKeys.LevelKey);
    private static readonly JsonEncodedText PropLevelRank = JsonEncodedText.Encode(Services.JsonOutputKeys.LevelRank);
    private static readonly JsonEncodedText PropCategory = JsonEncodedText.Encode("category");
    private static readonly JsonEncodedText PropMessageTemplate = JsonEncodedText.Encode(Services.JsonOutputKeys.MessageTemplate);
    private static readonly JsonEncodedText PropMessage = JsonEncodedText.Encode("message");
    private static readonly JsonEncodedText PropProperties = JsonEncodedText.Encode("properties");
    private static readonly JsonEncodedText PropContext = JsonEncodedText.Encode("context");
    private static readonly JsonEncodedText PropValue = JsonEncodedText.Encode("value");
    private static readonly JsonEncodedText PropCaptureMode = JsonEncodedText.Encode("capture_mode");
    private static readonly JsonEncodedText PropFormat = JsonEncodedText.Encode("format");
    private static readonly JsonEncodedText PropType = JsonEncodedText.Encode("type");
    private static readonly JsonEncodedText PropExMessage = JsonEncodedText.Encode("message");
    private static readonly JsonEncodedText PropStackTrace = JsonEncodedText.Encode(Services.JsonOutputKeys.StackTrace);
    private static readonly JsonEncodedText PropInner = JsonEncodedText.Encode("inner");

    public Utf8JsonFormatter(ILogLevelRegistry levelRegistry) {
        _levelRegistry = levelRegistry ?? throw new ArgumentNullException(nameof(levelRegistry));
    }

    public void Format(LogEvent logEvent, IBufferWriter<byte> output) {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(output);

        var registeredLevel = _levelRegistry.GetRegisteredLevel(logEvent.Level);

        // Pooled per-thread writer: lazy-init once, rebind to the caller's
        // buffer on every subsequent call. Reset(output) also clears depth
        // and token state, so the writer is left in the same shape a fresh
        // construction would produce.
        var writer = s_writer ??= new Utf8JsonWriter(output, s_writerOptions);
        writer.Reset(output);

        writer.WriteStartObject();

        writer.WriteString(PropTime, logEvent.TimeUtc);
        writer.WriteString(PropLevel, registeredLevel.Level.DisplayName);
        writer.WriteString(PropLevelKey, registeredLevel.Level.Key);
        writer.WriteNumber(PropLevelRank, registeredLevel.Rank);
        writer.WriteString(PropCategory, logEvent.Category.Value);
        writer.WriteString(PropMessageTemplate, logEvent.MessageTemplate);
        writer.WriteString(PropMessage, logEvent.Message);

        WriteProperties(writer, logEvent.Properties);
        WriteContext(writer, logEvent.Context);

        writer.WriteEndObject();
        writer.Flush();
    }

    /// <summary>
    /// Format a stack-allocated <see cref="LogEventBuffer"/> directly to
    /// UTF-8 bytes. No heap <see cref="LogEvent"/> materialization, no
    /// dictionary or property-array allocation — the buffer is read
    /// in place and the JSON is written straight to <paramref name="output"/>.
    ///
    /// <para>
    /// This is the kernel-fast-path overload. Sinks implementing
    /// <see cref="IKernelSink"/> can call it from
    /// <see cref="IKernelSink.Log(in LogEventBuffer)"/> to skip the
    /// chain-path materialization cost. Properties are written from
    /// whichever span the buffer holds (<see cref="LogEventBuffer.Properties"/>
    /// or <see cref="LogEventBuffer.CompactProperties"/>); the buffer
    /// has no Context dictionary, so the context section is omitted.
    /// </para>
    /// </summary>
    public void Format(in LogEventBuffer buffer, IBufferWriter<byte> output) {
        ArgumentNullException.ThrowIfNull(output);

        var registeredLevel = _levelRegistry.GetRegisteredLevel(buffer.Level);

        // Pooled per-thread writer: see Format(LogEvent, …) for the
        // contract. Reset(output) rebinds to the caller's buffer and
        // clears depth/token state.
        var writer = s_writer ??= new Utf8JsonWriter(output, s_writerOptions);
        writer.Reset(output);

        writer.WriteStartObject();

        writer.WriteString(PropTime, buffer.TimeUtc);
        writer.WriteString(PropLevel, registeredLevel.Level.DisplayName);
        writer.WriteString(PropLevelKey, registeredLevel.Level.Key);
        writer.WriteNumber(PropLevelRank, registeredLevel.Rank);
        writer.WriteString(PropCategory, buffer.Category.Value);
        writer.WriteString(PropMessageTemplate, buffer.MessageTemplate);
        writer.WriteString(PropMessage, buffer.Message);

        WriteBufferProperties(writer, buffer.Properties, buffer.CompactProperties);

        writer.WriteEndObject();
        writer.Flush();
    }

    private static void WriteBufferProperties(
        Utf8JsonWriter writer,
        ReadOnlySpan<LogProperty> properties,
        ReadOnlySpan<LogPropertyCompact> compactProperties)
    {
        writer.WriteStartObject(PropProperties);

        if (!properties.IsEmpty)
        {
            // Write directly from the span — no PropertyCollapser pass.
            // The kernel path produces properties that are already
            // single-occurrence (the typed-args dispatchers fill the
            // span once per slot); collapsing isn't needed.
            foreach (var property in properties)
            {
                writer.WriteStartObject(property.Name);
                writer.WriteString(PropValue, property.ResolvedValue?.ToString() ?? "null");
                writer.WriteString(PropCaptureMode, property.CaptureModeOrDefault.Value);
                if (!string.IsNullOrWhiteSpace(property.Format))
                {
                    writer.WriteString(PropFormat, property.Format!);
                }
                writer.WriteEndObject();
            }
        }
        else if (!compactProperties.IsEmpty)
        {
            // Compact span — dispatch on Kind so primitive values write
            // directly via the matching Utf8JsonWriter overload without
            // going through the lazy Value accessor (which boxes the
            // primitive and allocates a string on .ToString()).
            foreach (var property in compactProperties)
            {
                writer.WriteStartObject(property.Name);
                switch (property.Kind)
                {
                    case LogPropertyKind.Int32:
                        writer.WriteNumber(PropValue, (int)property.ScalarBits);
                        break;
                    case LogPropertyKind.Int64:
                        writer.WriteNumber(PropValue, property.ScalarBits);
                        break;
                    case LogPropertyKind.Double:
                        writer.WriteNumber(PropValue, BitConverter.Int64BitsToDouble(property.ScalarBits));
                        break;
                    case LogPropertyKind.Boolean:
                        writer.WriteBoolean(PropValue, property.ScalarBits != 0);
                        break;
                    case LogPropertyKind.DateTime:
                        writer.WriteString(PropValue,
                            new DateTime(property.ScalarBits, DateTimeKind.Utc));
                        break;
                    case LogPropertyKind.String:
                        writer.WriteString(PropValue, (string?)property.RefValue ?? "null");
                        break;
                    case LogPropertyKind.Null:
                        writer.WriteString(PropValue, "null");
                        break;
                    default:
                        writer.WriteString(PropValue, property.RefValue?.ToString() ?? "null");
                        break;
                }
                writer.WriteEndObject();
            }
        }

        writer.WriteEndObject();
    }

    private static void WriteProperties(Utf8JsonWriter writer, IReadOnlyList<LogProperty> properties) {
        writer.WriteStartObject(PropProperties);

        var collapsed = PropertyCollapser.Collapse(properties);

        foreach (var property in collapsed)
        {
            writer.WriteStartObject(property.Name);
            writer.WriteString(PropValue, property.ResolvedValue?.ToString() ?? "null");
            writer.WriteString(PropCaptureMode, property.CaptureModeOrDefault.Value);

            if (!string.IsNullOrWhiteSpace(property.Format))
            {
                writer.WriteString(PropFormat, property.Format!);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteContext(Utf8JsonWriter writer, IReadOnlyDictionary<string, object?> context) {
        writer.WriteStartObject(PropContext);

        using var sorted = SortedContextBuffer.Create(context);
        foreach (var pair in sorted.AsSpan())
        {
            if (pair.Value is Exception exception)
            {
                WriteException(writer, pair.Key, exception);
            }
            else
            {
                writer.WriteString(pair.Key, pair.Value?.ToString() ?? "null");
            }
        }

        writer.WriteEndObject();
    }

    private static void WriteException(Utf8JsonWriter writer, string key, Exception exception) {
        writer.WriteStartObject(key);
        writer.WriteString(PropType, exception.GetType().FullName ?? exception.GetType().Name);
        writer.WriteString(PropExMessage, exception.Message);
        writer.WriteString(PropStackTrace, exception.StackTrace ?? string.Empty);

        if (exception.InnerException is not null)
        {
            writer.WriteStartObject(PropInner);
            writer.WriteString(PropType,
                exception.InnerException.GetType().FullName ?? exception.InnerException.GetType().Name);
            writer.WriteString(PropExMessage, exception.InnerException.Message);
            writer.WriteString(PropStackTrace, exception.InnerException.StackTrace ?? string.Empty);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }
}
