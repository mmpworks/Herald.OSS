#nullable enable

using MMP.Herald.Templating.Strategies;

namespace MMP.Herald.Templating;

/// <summary>
/// Named presets for <see cref="ITemplateParseStrategy"/>. Pick one by
/// workload shape and hand to <see cref="MessageTemplateParser"/> at
/// construction.
///
/// Each factory returns a new strategy instance with its own cache state;
/// strategies are stateful and not intended to be shared across parsers.
/// </summary>
public static class TemplateParseStrategies
{
    /// <summary>
    /// L0 reference-equality → L1 cache → cold parse. Default.
    /// Best for game loops and any hot path where the same string literal
    /// fires repeatedly from the same call site.
    /// </summary>
    public static ITemplateParseStrategy LiteralFirst() => new LiteralFirstStrategy();

    /// <summary>
    /// L1 cache → cold parse. No reference-equality fast-path.
    /// Best for business and web workloads where call-site repetition is
    /// sparse and the L0 check would mostly miss.
    /// </summary>
    public static ITemplateParseStrategy CacheOnly() => new CacheOnlyStrategy();
}
