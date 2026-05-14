#nullable enable

using System;

namespace MMP.Herald.Templating.Strategies;

/// <summary>
/// L0 reference-equality slot → L1 concurrent dictionary → cold parse.
///
/// Best for workloads where the same string literal instance is handed to
/// Parse repeatedly from a hot loop — game frame loops, request handlers,
/// and any code where a call site fires many times per second. L0 hits are
/// a single field read plus <see cref="object.ReferenceEquals"/>, and cost
/// roughly 1 ns with zero allocation.
///
/// Thread safety: the L0 slot is written without synchronization. This is
/// safe because <see cref="MessageTemplate.Raw"/> is the exact string
/// reference handed to Parse; a stale read either hits on an identity that
/// matches the query (correct) or misses and falls through to L1 (correct).
/// On .NET reference-field writes are atomic, so there is no torn-read
/// window to worry about.
/// </summary>
public sealed class LiteralFirstStrategy : ITemplateParseStrategy
{
    private readonly ParseCache _cache = new();
    private MessageTemplate? _lastResult;

    public MessageTemplate Parse(string template) {
        ArgumentNullException.ThrowIfNull(template);

        // L0: identity check. ~1 ns when the same literal comes back in.
        var last = _lastResult;
        if (last is not null && ReferenceEquals(last.Raw, template))
            return last;

        // L1 + cold parse: shared with CacheOnlyStrategy via ParseCache.
        var result = _cache.GetOrTokenize(template);
        _lastResult = result;
        return result;
    }
}
