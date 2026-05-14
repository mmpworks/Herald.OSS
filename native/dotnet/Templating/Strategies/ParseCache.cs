#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace MMP.Herald.Templating.Strategies;

/// <summary>
/// Shared L1 cache + overflow-rotate + cold-parse helper used by every
/// <see cref="ITemplateParseStrategy"/> in this namespace. The cache is
/// the only state any strategy actually shares; lifting it out keeps
/// each strategy file focused on its own L0 policy (or absence thereof).
///
/// <para>Each strategy holds one of these and calls
/// <see cref="GetOrTokenize"/> on its miss path. Two callers, one
/// implementation — adding a third strategy reuses the same body.</para>
/// </summary>
internal sealed class ParseCache
{
    /// <summary>
    /// Cache-rotation threshold. When the dictionary grows past this
    /// the next miss swaps in a fresh dictionary so the old one becomes
    /// eligible for GC without a live-clear latency spike. Sized for
    /// the common case where a single application carries a few
    /// hundred distinct templates.
    /// </summary>
    private const int MaxParseCache = 512;

    private ConcurrentDictionary<string, MessageTemplate> _cache = new(StringComparer.Ordinal);
    private int _generation;

    /// <summary>
    /// Look up <paramref name="template"/> in the L1 cache; on miss,
    /// tokenize, insert, and return the parsed template. Allocation-free
    /// on hit (ConcurrentDictionary.TryGetValue does not allocate).
    /// </summary>
    public MessageTemplate GetOrTokenize(string template)
    {
        var cache = _cache;
        if (cache.TryGetValue(template, out var cached))
            return cached;

        // Miss path: swap to a fresh cache on overflow so the old
        // dictionary becomes eligible for GC once in-flight readers
        // finish — avoids the latency spike a live-clear would cause.
        if (cache.Count > MaxParseCache)
        {
            Interlocked.Increment(ref _generation);
            cache = new ConcurrentDictionary<string, MessageTemplate>(StringComparer.Ordinal);
            Interlocked.Exchange(ref _cache, cache);
        }

        var parsed = TemplateTokenizer.Tokenize(template);
        cache.TryAdd(template, parsed);
        return parsed;
    }
}
