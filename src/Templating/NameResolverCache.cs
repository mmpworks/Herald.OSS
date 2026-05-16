// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MMP.Herald.Templating;

/// <summary>
/// Process-static resolved-name cache used by the typed-args runtime dispatch
/// path. Maps <c>(policy.GetType(), template)</c> → a Herald-owned frozen
/// <see cref="string"/> array of resolved property names indexed by slot.
///
/// <para>
/// <b>Design choice — frozen-at-cap, not LRU.</b> A true LRU on the hot path
/// needs to update access-order on every cache hit, which means a write lock
/// (or at least a concurrent linked-list update) per dispatch. That destroys
/// the zero-allocation accept-path Herald is built around. Instead, the cache
/// fills up to <see cref="CapacityLimit"/> entries and then stops adding.
/// One-shot warning event fires the first time the cap is hit so an operator
/// can audit template cardinality or raise the cap.
/// </para>
///
/// <para>
/// <b>Cache key — <see cref="Type"/> identity, not <see cref="IPropertyNamingPolicy.Id"/>.</b>
/// Two custom policies that both report <c>Id = "kebab"</c> would silently
/// share a cache slot and corrupt each other's output. The <c>Type</c>
/// identity is unique by construction; <c>Id</c> stays in the public surface
/// for JSON round-trip and display only.
/// </para>
///
/// <para>
/// <b>Cold-miss validation.</b> When a policy's <c>ResolveAll</c> returns a
/// new array, the cache validates length and non-null entries before caching.
/// A contract violation is loud — it throws — so a buggy custom policy
/// surfaces at first call rather than silently corrupting events.
/// </para>
/// </summary>
public static class NameResolverCache
{
    /// <summary>
    /// Maximum number of <c>(policyType, template)</c> entries the cache
    /// will hold. Once full, further unique templates fall through to a
    /// per-call <c>ResolveAll</c> invocation (paying the cold-miss cost on
    /// every dispatch for that template). 8192 covers the closed-set
    /// expectation for app-code templates by a comfortable margin while
    /// keeping the worst-case memory bound to ~256 KB of references plus
    /// the string heap they point into.
    /// </summary>
    public const int CapacityLimit = 8192;

    private static readonly ConcurrentDictionary<(Type policyType, string template), string[]> _entries =
        new();

    private static int _onCapHitFired; // 0/1, set via Interlocked.Exchange.

    // Single MessageTemplateParser instance reused across the process.
    // Parsing is stateless aside from configuration; the default options
    // (LiteralFirst strategy, empty destructuring registry) are what the
    // generator-emitted dispatch path needs — destructuring only affects
    // rendering, not the property-name tokens we extract here.
    private static readonly MessageTemplateParser _parser = new();

    /// <summary>
    /// Fires exactly once, the first time an insert is dropped because the
    /// cache reached <see cref="CapacityLimit"/>. Carries the policy id and
    /// the offending template so an operator can audit cardinality. Re-arms
    /// only when <see cref="Reset"/> is called (tests / hot-reload).
    /// </summary>
    public static event Action<string, string>? OnCapacityHit;

    /// <summary>
    /// Cache-only lookup. Returns the cached resolved-name array when present,
    /// <c>false</c> otherwise. Used by the typed-args generated dispatchers to
    /// take a zero-allocation fast path on cache hit: the dispatcher only
    /// constructs the params-array of caller-argument-expression names when
    /// this method returns <c>false</c>.
    /// </summary>
    public static bool TryGetCached(
        IPropertyNamingPolicy policy,
        string template,
        out string[]? cached)
    {
        if (policy is null) throw new ArgumentNullException(nameof(policy));
        if (template is null) throw new ArgumentNullException(nameof(template));

        var key = (policy.GetType(), template);
        if (_entries.TryGetValue(key, out var result))
        {
            cached = result;
            return true;
        }
        cached = null;
        return false;
    }

    /// <summary>
    /// Resolve property names for one (policy, template) pair. On cache hit
    /// returns the cached array by reference, zero allocation. On miss, parses
    /// the template, calls <see cref="IPropertyNamingPolicy.ResolveAll"/>,
    /// validates the result, freezes it into a Herald-owned array, and caches
    /// it (when below the cap). When the cap is hit, the resolution still runs
    /// but the result is not cached.
    /// </summary>
    /// <remarks>
    /// This is the entry point the source-generated typed-args dispatchers
    /// call: they pass the raw template string + the caller-argument-
    /// expression names. Parsing happens once per unique template on the
    /// cold miss path, then never again for that template.
    /// </remarks>
    public static string[] Resolve(
        IPropertyNamingPolicy policy,
        string template,
        ReadOnlySpan<string> argExprs)
        => Resolve(policy, template, argExprs, out _);

    /// <summary>
    /// Diagnostics-aware no-token overload. Same parsing + caching semantics
    /// as <see cref="Resolve(IPropertyNamingPolicy,string,ReadOnlySpan{string})"/>;
    /// also reports whether the result came from the cache.
    /// </summary>
    public static string[] Resolve(
        IPropertyNamingPolicy policy,
        string template,
        ReadOnlySpan<string> argExprs,
        out bool wasCacheHit)
    {
        if (policy is null) throw new ArgumentNullException(nameof(policy));
        if (template is null) throw new ArgumentNullException(nameof(template));

        var key = (policy.GetType(), template);

        if (_entries.TryGetValue(key, out var cached))
        {
            wasCacheHit = true;
            return cached;
        }

        wasCacheHit = false;

        // Parse + extract property tokens once per unique template. The
        // resulting array is stack-friendly for typical arities (1-4) but
        // fall through to a heap array for longer templates; either way
        // the parse cost amortises to zero on every subsequent call.
        var parsed = _parser.Parse(template);
        var propertyTokens = ExtractPropertyTokens(parsed.Tokens);

        var resolved = ResolveAndValidate(policy, propertyTokens, argExprs);

        if (_entries.Count >= CapacityLimit)
        {
            FireCapHitOnce(policy.Id, template);
            return resolved;
        }

        return _entries.GetOrAdd(key, resolved);
    }

    /// <summary>
    /// Tokens-supplied overload. Used by tests that want to drive the cache
    /// with explicit token lists (e.g. exercising a policy against
    /// hand-constructed inputs without round-tripping through the parser).
    /// </summary>
    public static string[] Resolve(
        IPropertyNamingPolicy policy,
        string template,
        ReadOnlySpan<MessageTemplateToken.Property> tokens,
        ReadOnlySpan<string> argExprs)
        => Resolve(policy, template, tokens, argExprs, out _);

    /// <summary>
    /// Diagnostics-aware overload. Same resolution semantics as
    /// <see cref="Resolve(IPropertyNamingPolicy,string,ReadOnlySpan{MessageTemplateToken.Property},ReadOnlySpan{string})"/>;
    /// also reports whether the result came from the cache. Callers that
    /// track hit/miss diagnostics use this variant to avoid a second
    /// dictionary lookup.
    /// </summary>
    public static string[] Resolve(
        IPropertyNamingPolicy policy,
        string template,
        ReadOnlySpan<MessageTemplateToken.Property> tokens,
        ReadOnlySpan<string> argExprs,
        out bool wasCacheHit)
    {
        if (policy is null) throw new ArgumentNullException(nameof(policy));
        if (template is null) throw new ArgumentNullException(nameof(template));

        var key = (policy.GetType(), template);

        if (_entries.TryGetValue(key, out var cached))
        {
            wasCacheHit = true;
            return cached;
        }

        wasCacheHit = false;
        var resolved = ResolveAndValidate(policy, tokens, argExprs);

        // Frozen-at-cap: if the cache is already full, skip the insert and
        // fire the one-shot warning. The caller still gets a correct result;
        // the only consequence is paying the cold cost again next time.
        if (_entries.Count >= CapacityLimit)
        {
            FireCapHitOnce(policy.Id, template);
            return resolved;
        }

        // GetOrAdd handles the race where two threads miss simultaneously —
        // the loser's array is discarded, the winner's stays in the cache.
        return _entries.GetOrAdd(key, resolved);
    }

    /// <summary>
    /// Test / hot-reload helper. Clears all cached entries and rearms the
    /// capacity-hit warning. Not for production hot-path use.
    /// </summary>
    public static void Reset()
    {
        _entries.Clear();
        Interlocked.Exchange(ref _onCapHitFired, 0);
    }

    /// <summary>Current number of cached entries. Test-visibility only.</summary>
    public static int Count => _entries.Count;

    /// <summary>Snapshot of cached policy types. Diagnostic-only.</summary>
    public static IReadOnlyCollection<Type> CachedPolicyTypes =>
        _entries.Keys.Select(k => k.policyType).Distinct().ToArray();

    /// <summary>
    /// Filter the mixed token list down to just the property placeholders.
    /// Text tokens drive rendering (the human-readable message string) but
    /// have no role in property naming. Most templates have 0-16 property
    /// tokens; we accept the small allocation here rather than streaming
    /// because the result lands in the long-lived cache slot.
    /// </summary>
    private static MessageTemplateToken.Property[] ExtractPropertyTokens(
        IReadOnlyList<MessageTemplateToken> tokens)
    {
        // First count to size the array exactly — avoids a List<T> grow
        // sequence in the typical case. Cognitive Complexity note: the
        // single-pass form below would be cleaner with LINQ.OfType<>().
        // We avoid LINQ here because the cache populator is called once
        // per unique template and the allocation pattern matters at
        // process startup when N templates miss in a burst.
        var count = 0;
        foreach (var t in tokens)
        {
            if (t is MessageTemplateToken.Property) count++;
        }

        var result = new MessageTemplateToken.Property[count];
        var i = 0;
        foreach (var t in tokens)
        {
            if (t is MessageTemplateToken.Property p)
            {
                result[i++] = p;
            }
        }
        return result;
    }

    private static string[] ResolveAndValidate(
        IPropertyNamingPolicy policy,
        ReadOnlySpan<MessageTemplateToken.Property> tokens,
        ReadOnlySpan<string> argExprs)
    {
        var result = policy.ResolveAll(tokens, argExprs);

        if (result is null)
        {
            throw new InvalidOperationException(
                $"Naming policy '{policy.Id}' ({policy.GetType().FullName}) " +
                "returned null from ResolveAll. Policies must return a non-null array.");
        }

        if (result.Length != argExprs.Length)
        {
            throw new InvalidOperationException(
                $"Naming policy '{policy.Id}' ({policy.GetType().FullName}) " +
                $"returned an array of length {result.Length} for {argExprs.Length} " +
                "slots. Returned array length must equal argExprs length.");
        }

        for (var i = 0; i < result.Length; i++)
        {
            if (string.IsNullOrEmpty(result[i]))
            {
                throw new InvalidOperationException(
                    $"Naming policy '{policy.Id}' ({policy.GetType().FullName}) " +
                    $"returned a null or empty name at slot {i}. Names must be " +
                    "non-null and non-empty.");
            }
        }

        return result;
    }

    private static void FireCapHitOnce(string policyId, string template)
    {
        // CompareExchange so the warning fires exactly once across all
        // concurrent threads racing on a cap-hit. Subsequent cap-hits go
        // silent until Reset() rearms the flag.
        if (Interlocked.CompareExchange(ref _onCapHitFired, 1, 0) == 0)
        {
            OnCapacityHit?.Invoke(policyId, template);
        }
    }
}
