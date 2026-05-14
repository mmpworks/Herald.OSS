#nullable enable

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MMP.Herald.Configuration.Simple;

/// <summary>
/// Root document type for the "simple" pipeline config schema shared by
/// Herald.Lean and Herald.Embed. A single file can declare multiple named
/// pipelines; <see cref="SimpleConfigFactory.BuildAll"/> builds them all
/// in order.
/// </summary>
public sealed record SimpleLoggingConfig
{
    /// <summary>Declared pipelines. Empty list is a no-op.</summary>
    [JsonPropertyName("pipelines")]
    public List<SimplePipelineConfig> Pipelines { get; init; } = [];
}
