#nullable enable

using System.Diagnostics.CodeAnalysis;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Enrichers;
using MMP.Herald.Events;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Enrichers;

/// <summary>
/// Bridges a user-authored <see cref="ILogEventEnricher"/> into the native
/// <see cref="ILogEnricher"/> pipeline.
///
/// <para>
/// <b>Enrichment flow:</b>
/// <list type="number">
///   <item>
///     An enrichment-time <see cref="LogEvent"/> view is constructed from the
///     live <see cref="LogEventEnrichmentContext"/>. This view is NOT backed
///     by a finalized native event — it exposes the event's current fields
///     (level, template, already-added properties) as a Serilog-shaped surface.
///   </item>
///   <item>
///     A <see cref="LogEventPropertyFactoryShim"/> is constructed over the same
///     context. Every <c>CreateProperty</c> call on the shim forwards the
///     property to <see cref="LogEventEnrichmentContext.AddProperty"/> with the
///     correct <see cref="MMP.Herald.Templating.LogPropertyCaptureMode"/>.
///   </item>
///   <item>
///     The user enricher's <see cref="ILogEventEnricher.Enrich"/> is called with
///     both objects. Properties reach the native pipeline through the shim.
///   </item>
/// </list>
/// </para>
///
/// <para>
/// <b>Known gap — ToJsonConfig:</b> the round-trip emits the bare type name
/// <c>SerilogEnricherAdapter</c> followed by the wrapped type name. A rebuilt
/// pipeline cannot reconstruct the user enricher from JSON alone because
/// <see cref="ILogEventEnricher"/> has no Herald JSON-factory registration.
/// Pinned by test as a documented, non-silent gap.
/// </para>
/// </summary>
internal sealed class SerilogEnricherAdapter(ILogEventEnricher userEnricher) : ILogEnricher
{
    [SuppressMessage("Trimming", "IL2026",
        Justification = "Enricher bridge path uses reflection (value projection) by design.")]
    public void Enrich(LogEventEnrichmentContext context)
    {
        // Build the enrichment-time LogEvent view and the factory shim.
        // Both are constructed per-call (not cached) because the context is
        // mutable and each enricher in the chain gets an independent view
        // of the event state at the time it runs.
        var mirrorView = new MMP.Herald.Serilog.Events.LogEvent(context);
        var factoryShim = new LogEventPropertyFactoryShim(context);

        userEnricher.Enrich(mirrorView, factoryShim);
    }

    /// <summary>
    /// Serialises to a bare type-name token. A JSON-reconstructed pipeline
    /// cannot instantiate the user enricher because <see cref="ILogEventEnricher"/>
    /// has no Herald JSON-factory registration. This is a documented gap — the
    /// native <see cref="ILogEnricher.ToJsonConfig"/> doc on custom enrichers
    /// carries the same warning. Pinned by <c>CustomEnricherAdapterTests
    /// .ToJsonConfig_emits_bare_type_name_known_gap</c>.
    /// </summary>
    public JsonEnricherConfig ToJsonConfig() =>
        new($"{nameof(SerilogEnricherAdapter)}({userEnricher.GetType().Name})");
}
