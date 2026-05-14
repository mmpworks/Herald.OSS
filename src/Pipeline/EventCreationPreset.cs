#nullable enable

namespace MMP.Herald.Pipeline;

/// <summary>
/// Defines how log events are created before entering the decorator chain.
/// The pipeline (async, filtering, batching, sinks) is always the same —
/// the preset only controls event creation overhead.
///
/// Think of presets as "recipes" for the front of the pipeline:
///   Structured: full template parsing, enrichment, caller info (~576-1,091ns)
///   HotPath:    minimal — pre-formatted strings, no enrichment (~87ns)
///   Generated:  Roslyn-compiled templates, zero boilerplate (~600-900ns)
///
/// The decorator chain is identical regardless of preset. A physics tick
/// uses HotPath; a quest system uses Structured; both share the same
/// async queue, circuit breaker, and sinks.
/// </summary>
public enum EventCreationPreset
{
    /// <summary>
    /// Full-featured: template parsing, enrichers, scoped context, caller info capture.
    /// Use for system events, lifecycle tracking, business transitions.
    /// ~576-1,091ns per accepted event (BenchmarkDotNet).
    /// </summary>
    Structured,

    /// <summary>
    /// Bare-bones: pre-formatted strings only, no enrichment, no templates, no properties.
    /// Use for game loop hot paths where every nanosecond matters.
    /// ~87ns per accepted event (BenchmarkDotNet).
    /// All methods marked [AggressiveInlining].
    /// </summary>
    HotPath,

    /// <summary>
    /// Roslyn source-generated: [HeraldLog] attribute emits method bodies at compile time.
    /// Templates are pre-compiled, properties are baked in. Zero boilerplate.
    /// ~600-900ns per accepted event (estimated).
    /// </summary>
    Generated
}
