#nullable enable

using System;

namespace MMP.Herald.Templating.Strategies;

/// <summary>
/// L1 concurrent dictionary → cold parse. No reference-equality slot.
///
/// Best for workloads where call-site repetition is sparse — business
/// applications, web handlers, and batch jobs where the same literal is
/// not handed in back-to-back from a tight loop. L0 would mostly miss in
/// those shapes, so skipping the ref-equality check saves a branch per
/// call without losing any practical hit rate.
/// </summary>
public sealed class CacheOnlyStrategy : ITemplateParseStrategy
{
    private readonly ParseCache _cache = new();

    public MessageTemplate Parse(string template) {
        ArgumentNullException.ThrowIfNull(template);
        return _cache.GetOrTokenize(template);
    }
}
