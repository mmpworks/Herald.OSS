#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Configuration.Json;
/// <summary>
/// JSON-facing sink configuration.
/// Path is used by file sinks.
/// Uri is used by HTTP sinks.
/// Host and Port are used by TCP sinks.
/// Retry is optional and sink-local.
/// Label is consumed by the security registrar for external-source
/// registrations; null or empty triggers auto-generation at hoist time.
///
/// <para><b>RunState + loopback flags.</b> Mirror the runtime fields on
/// <c>LoggingRuntimeSinkDefinition</c>. <c>RunState</c> is serialized as
/// a string ("disabled" | "live" | "test") for human readability of
/// the on-disk JSON; the runtime mapper parses it back to the enum.
/// Defaults preserve pre-loopback behaviour: <c>RunState = "live"</c>,
/// no live teeing.</para>
///
/// <para><b>Properties bag (v2).</b> A sink's mmpform <c>__properties</c>
/// block names every property it consumes, with declared types and
/// defaults. Those values land here under <c>Properties</c> as a
/// generic dictionary so Core stays unaware of any specific sink's
/// shape. The sink's own provider reads from this bag at
/// <c>CreateSink</c> time. Legacy sinks that still drive their
/// configuration through the typed fields above (<c>Path</c>,
/// <c>Rolling</c>, etc.) leave <c>Properties</c> null until they
/// migrate.</para>
/// </summary>
public sealed record JsonLogSinkConfig(
    string Name,
    string Kind,
    bool Enabled = true,
    string? Path = null,
    string? Alias = null,
    string? Vendor = null,
    string? Version = null,
    string? Uri = null,
    string? Host = null,
    int? Port = null,
    JsonRetryPolicyConfig? Retry = null,
    JsonFileRollingConfig? Rolling = null,
    string? MinLevel = null,
    string? OutputTemplate = null,
    bool IsAudit = false,
    bool EnableMetrics = false,
    string? Label = null,
    string? RunState = null,
    string? TestOutFileSuffix = null,
    bool TeeLiveToFile = false,
    bool TeeLiveToUrl = false,
    IReadOnlyDictionary<string, object?>? Properties = null);