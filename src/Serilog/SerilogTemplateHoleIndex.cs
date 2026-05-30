// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MMP.Herald.Templating;

namespace MMP.Herald.Serilog;

/// <summary>
/// Process-static positional hole-name + capture-mode index for the Serilog
/// naming rule. Populated once per unique template string, then read at O(1)
/// cost on every subsequent dispatch.
///
/// <para>
/// <b>Serilog naming rule.</b> The hole name comes from the template token,
/// not from the call-site variable expression. For
/// <c>"User {Id} ordered {@Order}"</c>, hole 0 is <c>Id</c> and hole 1 is
/// <c>Order</c>. If there are more arguments than holes, the extra args are
/// unnamed and fall back to their positional index string (<c>"0"</c>,
/// <c>"1"</c>, …). Positional numeric tokens (<c>{0}</c>, <c>{1}</c>) are
/// valid hole names in Serilog and are stored verbatim.
/// </para>
///
/// <para>
/// <b>Capture mode.</b> <c>{@Name}</c> carries
/// <see cref="LogPropertyCaptureMode.Destructure"/>; <c>{$Name}</c> carries
/// <see cref="LogPropertyCaptureMode.Stringify"/>; bare <c>{Name}</c> carries
/// <see cref="LogPropertyCaptureMode.Default"/>. The
/// <see cref="IsDefaultModeAt"/> predicate returns <c>true</c> for the
/// default-mode path so the generated overloads can select the zero-alloc
/// compact buffer path on all-default calls and fall back to the full
/// <see cref="LogProperty"/> path for destructuring / stringify holes.
/// </para>
///
/// <para>
/// <b>Bounded cache — frozen-at-cap, not LRU.</b> Mirrors the discipline in
/// <see cref="NameResolverCache"/>: fills up to <see cref="CapacityLimit"/>
/// entries then stops inserting. Only interned template strings (compile-time
/// literals) are eligible for caching — a runtime-built template gets a fresh
/// reference every call and would never hit the cache again, so inserting it
/// would march the cap without benefit. Non-interned templates fall through to
/// a per-call parse that is still correct, just not cached.
/// </para>
/// </summary>
public sealed class SerilogTemplateHoleIndex
{
    /// <summary>
    /// Maximum number of template entries the cache will hold. 4096 covers
    /// the expected closed-set of call-site templates while bounding the
    /// worst-case memory to ~128 KB of references plus their string heap.
    /// </summary>
    public const int CapacityLimit = 4096;

    // Process-static singleton — the generator emits references to
    // SerilogTemplateHoleIndex.Instance so there is no allocation per call.
    private static readonly SerilogTemplateHoleIndex _instance = new();

    /// <summary>Process-static singleton instance.</summary>
    public static SerilogTemplateHoleIndex Instance => _instance;

    // Cache key is the template string reference. ConcurrentDictionary
    // handles the race where two threads miss simultaneously — the loser's
    // entry is discarded.
    private readonly ConcurrentDictionary<string, HoleEntry> _cache = new(
        concurrencyLevel: Environment.ProcessorCount,
        capacity: 256,
        comparer: StringComparer.Ordinal);

    private readonly MessageTemplateParser _parser = new();

    private SerilogTemplateHoleIndex() { }

    /// <summary>
    /// Return the hole name at position <paramref name="i"/> for the given
    /// <paramref name="template"/>. O(1) after the first parse of this template.
    /// Falls back to the positional index string when <paramref name="i"/> is
    /// beyond the template's hole count.
    /// </summary>
    public string NameAt(string template, int i)
    {
        var entry = Resolve(template);
        if (i < entry.Names.Length) return entry.Names[i];
        // Extra args beyond hole count: fall back to positional index string.
        return i.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Returns <c>true</c> when the hole at position <paramref name="i"/>
    /// has <see cref="LogPropertyCaptureMode.Default"/> capture mode (bare
    /// <c>{Name}</c>, no <c>@</c> or <c>$</c> prefix). Returns <c>true</c>
    /// for out-of-range indices (no hole = no non-default capture).
    /// </summary>
    public bool IsDefaultModeAt(string template, int i)
    {
        var entry = Resolve(template);
        if (i < entry.IsDefaultMode.Length) return entry.IsDefaultMode[i];
        // Beyond hole count — no capture mode to override; treat as default.
        return true;
    }

    // ── Core resolve/cache logic ────────────────────────────────────────────

    private HoleEntry Resolve(string template)
    {
        if (_cache.TryGetValue(template, out var cached))
            return cached;

        return ResolveAndCache(template);
    }

    private HoleEntry ResolveAndCache(string template)
    {
        var entry = Parse(template);

        // Frozen-at-cap + interned-only: same discipline as NameResolverCache.
        // Non-interned templates skip the insert — their reference is unstable
        // and would never produce a cache hit on the next call.
        if (string.IsInterned(template) is not null && _cache.Count < CapacityLimit)
        {
            _cache.TryAdd(template, entry);
        }

        return entry;
    }

    private HoleEntry Parse(string template)
    {
        var parsed = _parser.Parse(template);

        // Collect property tokens in declaration order.
        // Cognitive complexity note: single linear pass, no branches inside loop.
        var nameList = new List<string>();
        var modeList = new List<bool>();

        foreach (var token in parsed.Tokens)
        {
            if (token is MessageTemplateToken.Property prop)
            {
                nameList.Add(prop.Name);
                modeList.Add(prop.CaptureMode == LogPropertyCaptureMode.Default);
            }
        }

        return new HoleEntry(nameList.ToArray(), modeList.ToArray());
    }

    // ── Value type for the cache entry ─────────────────────────────────────

    /// <summary>
    /// Frozen arrays for one parsed template. Allocated once per unique
    /// template; reads are lock-free array-index accesses after that.
    /// </summary>
    private readonly struct HoleEntry
    {
        internal readonly string[] Names;
        internal readonly bool[] IsDefaultMode;

        internal HoleEntry(string[] names, bool[] isDefaultMode)
        {
            Names = names;
            IsDefaultMode = isDefaultMode;
        }
    }

    // ── Test / reset surface ────────────────────────────────────────────────

    /// <summary>
    /// Clear the cache. Intended for test isolation and hot-reload
    /// scenarios. Not for production hot-path use.
    /// </summary>
    public void Reset() => _cache.Clear();

    /// <summary>Current number of cached entries. Diagnostic / test use only.</summary>
    public int Count => _cache.Count;
}
