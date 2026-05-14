#nullable enable

namespace MMP.Herald.Services;

/// <summary>
/// Well-known registration names for built-in event processors.
/// Plugins should define their own namespace-prefixed names to avoid collisions.
///
/// Convention: "vendor.category.name" (e.g., "myPlugin.security.redaction")
///
/// Usage:
///   // Built-in processor
///   builder.WithEventProcessor(KnownProcessorNames.CompiledRedaction, processor);
///
///   // Plugin processor (use your own namespace)
///   builder.WithEventProcessor("myPlugin.analytics.sampler", sampler, protect: true);
///
///   // Remove by name
///   builder.WithoutEventProcessor(KnownProcessorNames.CompiledRedaction);
/// </summary>
public static class KnownProcessorNames
{
    /// <summary>Built-in compiled redaction processor.</summary>
    public const string CompiledRedaction = "herald.redaction.compiled";

    /// <summary>Log deduplication processor (addon).</summary>
    public const string LogDeduplication = "herald.dedup";

    /// <summary>Adaptive sampling filter (addon).</summary>
    public const string AdaptiveSampling = "herald.sampling.adaptive";

    /// <summary>Log metric extractor (addon).</summary>
    public const string MetricExtraction = "herald.metrics.extraction";
}
