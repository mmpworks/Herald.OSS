#nullable enable

using System.Collections.Concurrent;
using System.Collections.Generic;
using MMP.Herald.Routing;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Instance-scoped component-schema registry. The static
/// <see cref="ComponentSchemaRegistry"/> facade forwards every call to
/// the default host's instance; tests and multi-tenant hosts that need
/// isolation construct their own <c>HeraldHost</c> and use
/// <c>host.ComponentSchemas</c> directly.
///
/// <para>
/// Deliberately lacks <c>Unregister</c> / <c>IsRegistered</c> parity with
/// the JSON-kind registries: schemas are append-only metadata the
/// dashboard reads at render time. Removal would break a live dashboard
/// mid-render and there is no test path that needs it.
/// </para>
/// </summary>
public sealed class ComponentSchemaKindRegistry
{
    // "swappable" has no entry here. Its only configurable knob
    // (debounceMs) lives on JsonHotReloadConfig, not on a per-step schema —
    // the dashboard reads it from the hot-reload section directly.
    //
    // Apache-tier schemas are seeded here. Pro/Enterprise plugin
    // bootstraps call Register(...) at module-init time to add their
    // owned schemas (circuitBreaker, retry, durableBuffer, fallback,
    // audit). Without those plugin assemblies loaded, GetSchema returns
    // null for the corresponding step name — the dashboard renders a
    // "schema not loaded" placeholder.
    private readonly ConcurrentDictionary<string, IReadOnlyList<SinkConfigField>> _schemas =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["async"] = AsyncLogger.DefaultSchema,
            ["fanOut"] = SafeCompositeLogger.DefaultSchema,
            ["eventProcessing"] = EventProcessingLogger.DefaultSchema,
            ["filtering"] = Filters.FilteringLogger.DefaultSchema,
            ["flightRecorder"] = Addons.GamePerformance.FlightRecorderLogger.DefaultSchema,
            ["frameBudget"] = Addons.GamePerformance.FrameBudgetLogger.DefaultSchema,
            ["preset:structured"] = StructuredLogger.DefaultSchema,
        };

    public IReadOnlyList<SinkConfigField>? GetSchema(string stepName) =>
        _schemas.TryGetValue(stepName, out var schema) ? schema : null;

    public void Register(string stepName, IReadOnlyList<SinkConfigField> schema) =>
        _schemas[stepName] = schema;
}
