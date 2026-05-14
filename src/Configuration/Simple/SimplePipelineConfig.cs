#nullable enable

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MMP.Herald.Configuration.Simple;

/// <summary>
/// Per-pipeline descriptor for the "simple" config schema shared by
/// Herald.Lean and Herald.Embed. Mirrors what those facades have accepted
/// on their own; hoisting the shape into Core removes the duplicated
/// builder-wiring logic that previously lived in each facade.
///
/// Properties are optional service-identity fields read when the "service"
/// enricher is enabled:
///   <c>serviceName</c>, <c>serviceVersion</c>, <c>serviceRegion</c>,
///   <c>serviceEnvironment</c>.
/// When no <c>serviceName</c> is supplied, the factory falls back to the
/// pipeline's <see cref="Name"/>.
/// </summary>
public sealed record SimplePipelineConfig
{
    /// <summary>Pipeline registration name. Must be unique within the tenant.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "default";

    /// <summary>
    /// Optional tenant id this pipeline belongs to. Empty (the default)
    /// means the registration lands in <c>HeraldTenant.Default</c>, which
    /// is the single-tenant shape every existing config file produces.
    /// Non-default tenants require the Enterprise edition; the registry
    /// throws at registration time on Community and Pro builds.
    /// </summary>
    [JsonPropertyName("tenant")]
    public string Tenant { get; init; } = "";

    /// <summary>Minimum log level admitted into the pipeline (trace, debug, info, warn, error, fatal).</summary>
    [JsonPropertyName("minimumLevel")]
    public string MinimumLevel { get; init; } = "info";

    /// <summary>Pipeline strategy preset: <c>default</c>, <c>filter-early</c>, or <c>minimal</c>.</summary>
    [JsonPropertyName("strategy")]
    public string Strategy { get; init; } = "default";

    /// <summary>Sinks registered on the pipeline. Empty means no sink — the validator will reject that.</summary>
    [JsonPropertyName("sinks")]
    public List<SimpleSinkConfig> Sinks { get; init; } = [];

    /// <summary>Enricher names: machine, process, thread (no-ops — always on), correlation, service.</summary>
    [JsonPropertyName("enrichers")]
    public List<string> Enrichers { get; init; } = [];

    /// <summary>Free-form key/value map. Currently consumed for service-identity fields.</summary>
    [JsonPropertyName("properties")]
    public Dictionary<string, string> Properties { get; init; } = new();
}
