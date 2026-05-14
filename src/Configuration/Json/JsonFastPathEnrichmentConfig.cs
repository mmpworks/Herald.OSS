#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing configuration for the kernel-aware
/// <see cref="MMP.Herald.Pipeline.Kernel.FastPathEnricher"/>. Carries the
/// list of static (constant-value) properties to append on every accepted
/// event.
///
/// <para>
/// Scope mirrors the runtime path: static properties only. Dynamic
/// per-call values (traceId, request-scoped properties) belong on the
/// legacy <c>Enrichers</c> path (<see cref="JsonEnricherConfig"/>).
/// </para>
/// </summary>
public sealed record JsonFastPathEnrichmentConfig(
    IReadOnlyList<JsonFastPathEnrichmentEntry> Properties);

/// <summary>One static property in <see cref="JsonFastPathEnrichmentConfig"/>.</summary>
/// <remarks>
/// <see cref="Value"/> is held as a <see cref="string"/> in the JSON
/// shape. Reconstruction wraps it in a <c>LogProperty</c> with
/// <see cref="object"/> typing so the runtime sees the same shape as
/// fluent-API construction.
/// </remarks>
public sealed record JsonFastPathEnrichmentEntry(
    string Name,
    string Value);
