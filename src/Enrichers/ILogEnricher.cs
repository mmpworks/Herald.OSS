#nullable enable

using MMP.Herald.Configuration.Json;
using MMP.Herald.Events;

namespace MMP.Herald.Enrichers;

/// <summary>
/// Adds context and properties during log event creation.
/// </summary>
public interface ILogEnricher
{
    void Enrich(LogEventEnrichmentContext context);

    /// <summary>
    /// Serialize this enricher to its JSON-shaped config record. The default
    /// implementation emits <c>Kind</c> only — suitable for parameter-less
    /// enrichers (CorrelationIdEnricher, ActivityEnricher, MachineNameLogEnricher,
    /// ProcessIdLogEnricher, ThreadIdLogEnricher, ExceptionDetailEnricher).
    /// Stateful enrichers must override and include their constructor
    /// parameters in <see cref="JsonEnricherConfig.Properties"/> so that
    /// <c>Reload(json)</c> reconstructs an equivalent instance — without
    /// the override the enricher round-trips as a bare type name and the
    /// rebuilt pipeline silently runs without its identity tags.
    /// </summary>
    JsonEnricherConfig ToJsonConfig() => new(GetType().Name);
}
