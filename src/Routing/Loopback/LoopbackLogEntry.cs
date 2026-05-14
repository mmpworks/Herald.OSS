#nullable enable

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MMP.Herald.Routing.Loopback;

/// <summary>
/// Wire shape for one loopback event. The interceptor projects each
/// <see cref="MMP.Herald.Events.LogEvent"/> onto this DTO before
/// publishing on the bus, writing to the rolling file, and posting to
/// the URL — that way every loopback consumer (file reader, URL
/// receiver, Dashboard SSE subscriber) sees identical bytes.
///
/// <para>Field set is intentionally narrow: timestamp, level, category,
/// message, properties. The dashboard's loopback panel asked for "just
/// the output from the sink" with property colouring — the markers it
/// already has (level, time) are produced separately by Live Logs.</para>
/// </summary>
public sealed record LoopbackLogEntry(
    long TimestampUnixMs,
    string Level,
    string? Category,
    string Message,
    Dictionary<string, object?>? Properties)
{
    /// <summary>
    /// True when this entry represents an event the wrapper rejected
    /// before it reached the inner sink (per-sink minimum-level gate
    /// kicked in). Bus-only — rejection entries never write to the
    /// loopback file leg or post to the URL receiver; they just
    /// surface to the dashboard's loopback panel so the operator can
    /// see why a sink isn't producing. Default false for accepted
    /// entries.
    /// </summary>
    public bool Rejected { get; init; }

    /// <summary>
    /// When <see cref="Rejected"/> is true, the canonical drop-reason
    /// string (currently <c>"level"</c> — disabled state never publishes,
    /// so that reason is not used here). Null on accepted entries.
    /// </summary>
    public string? RejectionReason { get; init; }
}

/// <summary>
/// Source-generated JSON context for AOT-safe NDJSON serialization of
/// <see cref="LoopbackLogEntry"/>. Reflection-based JSON would also
/// work, but Herald.Core is AOT-clean by policy and sinks live on the
/// hot path; the source-gen avoids reflection at every event.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Default,
    WriteIndented = false)]
[JsonSerializable(typeof(LoopbackLogEntry))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
internal partial class LoopbackJsonContext : JsonSerializerContext
{
}
