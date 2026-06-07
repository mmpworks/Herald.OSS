#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using Google.Protobuf;
using MMP.Herald.Events;
using MMP.Herald.Levels;

namespace MMP.Herald.Addons.OtlpSinks;

/// <summary>
/// Unified entry point for decoding OTLP log payloads into Herald
/// <see cref="LogEvent"/> instances. One surface for ingest callers that
/// should not have to know whether the payload arrived as protobuf or as
/// OTLP/HTTP JSON.
///
/// <para>
/// Delegates to <see cref="OtlpProtobufLogDecoder"/> for binary payloads and
/// <see cref="OtlpJsonDecoder"/> for JSON ones. Both decoders already
/// normalise the event shape — body → <c>Message</c>, record attributes →
/// <c>Properties</c>, resource attributes → <c>Context</c>, trace_id / span_id
/// → <c>Context</c> under <see cref="Services.LogContextKeys.TraceId"/> and
/// <see cref="Services.LogContextKeys.SpanId"/>.
/// </para>
///
/// <para>
/// Every malformed input lands as <see cref="InvalidOperationException"/>
/// with the underlying parser exception as <c>InnerException</c>. Ingest
/// endpoints can therefore catch a single exception type and turn it into
/// a 400 without reaching into Google.Protobuf or System.Text.Json
/// internals.
/// </para>
/// </summary>
public static class OtlpLogsDecoder
{
    /// <summary>
    /// Decode a serialized <c>ExportLogsServiceRequest</c> protobuf payload.
    /// OTLP severity is optional; records with no resolvable level fall back to
    /// <paramref name="optionalLevelDefault"/> (the pipeline's current minimum)
    /// when supplied, else <c>information</c>.
    /// </summary>
    public static IReadOnlyList<LogEvent> Decode(
        ReadOnlySpan<byte> protobuf, ILogLevelRegistry levelRegistry, LogLevel? optionalLevelDefault = null)
    {
        ArgumentNullException.ThrowIfNull(levelRegistry);

        // CodedInputStream takes a byte[]; the underlying decoder copies once.
        var bytes = protobuf.ToArray();
        try
        {
            return OtlpProtobufLogDecoder.Decode(bytes, levelRegistry, optionalLevelDefault);
        }
        catch (InvalidProtocolBufferException ex)
        {
            throw new InvalidOperationException(
                $"Malformed OTLP protobuf payload: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Decode an <c>ExportLogsServiceRequest</c> payload expressed as
    /// OTLP/HTTP JSON.
    /// OTLP severity is optional; records with no resolvable level fall back to
    /// <paramref name="optionalLevelDefault"/> (the pipeline's current minimum)
    /// when supplied, else <c>information</c>.
    /// </summary>
    public static IReadOnlyList<LogEvent> DecodeJson(
        string json, ILogLevelRegistry levelRegistry, LogLevel? optionalLevelDefault = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(levelRegistry);

        try
        {
            return OtlpJsonDecoder.Decode(json, levelRegistry, optionalLevelDefault);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Malformed OTLP JSON payload: {ex.Message}", ex);
        }
    }
}
