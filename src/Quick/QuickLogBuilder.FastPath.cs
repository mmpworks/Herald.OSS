#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Pipeline.Processors;

namespace MMP.Herald.Quick;

// Kernel-aware "fast path" configuration. Six methods extracted from
// QuickLogBuilder.With.cs (principal-review queue #19) along Rosanne's
// seam map. These setters all live on the kernel-eligible dispatch
// surface — installing any of them keeps the pipeline on the compact
// path rather than falling back through the event-object chain. The
// trade-off comments on each method explain how their "fast" form
// differs from the legacy sibling on Pipeline.cs.
public sealed partial class QuickLogBuilder
{
    /// <summary>
    /// Install kernel-aware redaction. Rules run on the property span before
    /// the <see cref="MMP.Herald.Pipeline.Kernel.LogEventBuffer"/> is
    /// constructed and stay on the kernel fast path — they do not register
    /// as <see cref="ILogEventProcessor"/> instances and do not move the
    /// pipeline off the compact dispatch path.
    ///
    /// <para>
    /// Trade-off vs <see cref="WithCompiledRedaction(CompiledRedactionRule[])"/>:
    /// the fast path supports only the exact-name + Remove / Mask / Hash
    /// subset of the rule API. Pattern rules (glob / regex), event-action
    /// rules (DropEvent / ReplaceMessage), and value-pattern conditions
    /// belong on the heavier <see cref="CompiledRedactionProcessor"/>;
    /// passing them here throws at builder time. Most production redaction
    /// shapes are exact-name only, so this is the dominant case.
    /// </para>
    ///
    /// <para>
    /// Both convenience methods can be combined when a pipeline has both
    /// a hot-path subset (handled here, kernel-eligible) and a long-tail
    /// subset (handled by <see cref="WithCompiledRedaction"/>, event-pipeline
    /// path). The two redactors run independently — fast first, processor
    /// second — but a pipeline that uses both is no longer kernel-eligible
    /// because the event processor disqualifies it. The fast redactor
    /// still runs in that mode, but the structural floor is paid anyway,
    /// so combining the two only makes sense when the long tail is
    /// genuinely required.
    /// </para>
    /// </summary>
    public QuickLogBuilder WithFastRedaction(params CompiledRedactionRule[] rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.Length == 0) return this;

        _fastRedactionRules ??= new List<CompiledRedactionRule>(rules.Length);
        _fastRedactionRules.AddRange(rules);
        return this;
    }

    /// <summary>
    /// Install kernel-aware 1-in-N sampling. The sampler runs at the
    /// dispatch boundary before any <see cref="MMP.Herald.Pipeline.Kernel.LogEventBuffer"/>
    /// is constructed — dropped events pay only one
    /// <see cref="System.Threading.Interlocked.Increment(ref long)"/> and
    /// one modulo, no allocation, no kernel call.
    ///
    /// <para>
    /// Trade-off vs <see cref="WithSampling(int)"/>: the legacy filter
    /// reads from a fully-materialised <see cref="Events.LogEvent"/> and
    /// disqualifies the kernel fast path entirely. The fast sampler stays
    /// kernel-eligible, but only models the "1 in N for everything" case —
    /// per-category scopes, max-per-second throttling, and consistent-hash
    /// shapes still belong on <see cref="WithSampling"/> /
    /// <see cref="Filters.CompositeSamplingFilter"/>.
    /// </para>
    /// </summary>
    public QuickLogBuilder WithFastSampling(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate), sampleRate,
                "Sample rate must be greater than zero.");
        }
        _fastSampleRate = sampleRate;
        return this;
    }

    /// <summary>
    /// Append a fixed set of <see cref="MMP.Herald.Templating.LogProperty"/>
    /// entries to every accepted event on the kernel fast path. The
    /// enricher allocates one new property array per call (sized
    /// <c>input + properties.Length</c>) and never disqualifies the kernel.
    ///
    /// <para>
    /// Trade-off vs registering a custom <see cref="Enrichers.ILogEnricher"/>:
    /// the legacy enricher path mutates a heap-allocated
    /// <see cref="Events.LogEventEnrichmentContext"/> the chain materialises
    /// before the enricher runs. The fast path skips the chain entirely
    /// for static (constant-value) enrichment — host name, service name,
    /// deployment environment, fixed tag set. Dynamic enrichers that
    /// compute a value per call (traceId, request-scoped properties)
    /// stay on the legacy path until a separate experiment validates
    /// that shape.
    /// </para>
    /// </summary>
    public QuickLogBuilder WithFastEnrichment(params MMP.Herald.Templating.LogProperty[] properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (properties.Length == 0) return this;

        _fastEnrichmentProperties ??= new List<MMP.Herald.Templating.LogProperty>(properties.Length);
        _fastEnrichmentProperties.AddRange(properties);
        return this;
    }

    /// <summary>
    /// Install a kernel-aware dynamic-level resolver backed by a
    /// <see cref="MMP.Herald.Levels.LogLevelSwitch"/>. The switch's
    /// <see cref="MMP.Herald.Levels.LogLevelSwitch.MinimumLevel"/> can be
    /// mutated at runtime and every subsequent accepted call reads the
    /// current value via a single volatile-load + frozen-map lookup.
    ///
    /// <para>
    /// Trade-off vs <see cref="WithDynamicLevels()"/>: the legacy
    /// dynamic-level path goes through
    /// <see cref="MMP.Herald.Configuration.DynamicLevelPolicy"/>, which
    /// disqualifies the kernel fast path entirely. The fast resolver
    /// stays kernel-eligible. Per-category overrides
    /// (<see cref="MMP.Herald.Levels.CategoryLevelSwitchMap"/>) ride
    /// alongside the global switch via the second overload.
    /// </para>
    /// </summary>
    public QuickLogBuilder WithFastDynamicLevel(MMP.Herald.Levels.LogLevelSwitch levelSwitch)
    {
        ArgumentNullException.ThrowIfNull(levelSwitch);
        _fastDynamicLevelSwitch = levelSwitch;
        _fastDynamicLevelCategoryMap = null;
        return this;
    }

    /// <summary>
    /// Wrap this pipeline's sink fan-out in a kernel-aware async sink so
    /// the caller's <c>Log(...)</c> returns immediately once the buffer
    /// is materialised and enqueued — a background consumer drains the
    /// queue and forwards each event to the original sink composite.
    ///
    /// <para>
    /// <paramref name="boundedCapacity"/> sets the channel's drop-write
    /// threshold. Producers exceeding the cap have their events
    /// silently discarded by the channel (current accounting limitation
    /// — see future-direction.md A3 for the planned drop-counter fix).
    /// 4096 is a reasonable starting point for production traffic; the
    /// bench uses the same value for the head-to-head comparison.
    /// </para>
    ///
    /// <para>
    /// <b>Topology.</b> A single FastPathAsyncSink wraps the entire
    /// routed-sinks composite. One channel, one background consumer
    /// thread, sequential fan-out inside the consumer. A slow inner
    /// sink can stall the others because they share the consumer; if
    /// you need per-sink isolation, see future-direction.md for the
    /// per-sink-wrapper variant.
    /// </para>
    ///
    /// <para>
    /// <b>Hot-reload.</b> Each reload retires the current async wrapper
    /// (drain in-flight events on the old composite) and installs a new
    /// wrapper with the JSON-supplied capacity. No event is lost during
    /// the swap.
    /// </para>
    /// </summary>
    public QuickLogBuilder WithFastAsyncSink(int boundedCapacity = 4096)
    {
        if (boundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(boundedCapacity), boundedCapacity,
                "Bounded capacity must be greater than zero.");
        }
        _fastAsyncSinkCapacity = boundedCapacity;
        return this;
    }

    /// <summary>
    /// Install a kernel-aware dynamic-level resolver with both a global
    /// switch and a per-category override map. When an event's category
    /// has an override in <paramref name="categoryMap"/>, the override's
    /// minimum level governs acceptance for that event; otherwise the
    /// global <paramref name="levelSwitch"/> governs.
    ///
    /// <para>
    /// Both inputs are mutable at runtime — caller can flip the global
    /// switch's level and add / remove / change category-specific levels
    /// via <see cref="MMP.Herald.Levels.CategoryLevelSwitchMap.SetCategoryLevel"/>
    /// and <see cref="MMP.Herald.Levels.CategoryLevelSwitchMap.RemoveCategoryOverride"/>.
    /// The kernel reads through to the live state on every accepted call.
    /// </para>
    ///
    /// <para>
    /// Cost when categories are configured: one extra
    /// <c>ConcurrentDictionary.TryGetValue</c> per accepted event; categories
    /// with no override land back on the same global path the single-arg
    /// overload uses. Configurations with no map carry no extra cost (the
    /// resolver branches on a null field set at construction).
    /// </para>
    /// </summary>
    public QuickLogBuilder WithFastDynamicLevel(
        MMP.Herald.Levels.LogLevelSwitch levelSwitch,
        MMP.Herald.Levels.CategoryLevelSwitchMap categoryMap)
    {
        ArgumentNullException.ThrowIfNull(levelSwitch);
        ArgumentNullException.ThrowIfNull(categoryMap);
        _fastDynamicLevelSwitch = levelSwitch;
        _fastDynamicLevelCategoryMap = categoryMap;
        return this;
    }
}
