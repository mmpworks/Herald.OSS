// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using MMP.Herald.Diagnostics;
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
/// <see cref="LogPropertyCaptureMode.Default"/>. <see cref="CaptureModeAt"/>
/// returns the per-hole mode so the generated overloads can pack it into the
/// compact slot — every hole rides the zero-alloc compact buffer regardless of
/// capture mode. <see cref="IsDefaultModeAt"/> derives from
/// <see cref="CaptureModeAt"/> for callers that only need the default predicate.
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

    // Throttle window for the cap-hit RuntimeNotice. Mirrors NameResolverCache:
    // once the cache reaches CapacityLimit, every subsequent unique interned
    // template re-parses on each dispatch. A multi-tenant host that crossed the
    // cap weeks ago keeps getting a fresh Warning every CapHitNoticeThrottleMs so
    // the "silent cliff after the cap" regression stays observable from a poll.
    // Bound to once-per-second so a burst of cold-miss dispatches cannot flood
    // the channel. Environment.TickCount64.
    private long _lastCapHitNoticeTicks;
    private const long CapHitNoticeThrottleMs = 1000;

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
    /// Return the capture mode at position <paramref name="i"/> for the given
    /// <paramref name="template"/>. <c>{@Name}</c> yields
    /// <see cref="LogPropertyCaptureMode.Destructure"/>, <c>{$Name}</c> yields
    /// <see cref="LogPropertyCaptureMode.Stringify"/>, bare <c>{Name}</c> yields
    /// <see cref="LogPropertyCaptureMode.Default"/>. Out-of-range indices (extra
    /// args beyond the hole count) resolve to
    /// <see cref="LogPropertyCaptureMode.Default"/>.
    /// </summary>
    public LogPropertyCaptureMode CaptureModeAt(string template, int i)
    {
        var entry = Resolve(template);
        return i < entry.CaptureModes.Length ? entry.CaptureModes[i] : LogPropertyCaptureMode.Default;
    }

    /// <summary>
    /// Returns <c>true</c> when the hole at position <paramref name="i"/>
    /// has <see cref="LogPropertyCaptureMode.Default"/> capture mode (bare
    /// <c>{Name}</c>, no <c>@</c> or <c>$</c> prefix). Returns <c>true</c>
    /// for out-of-range indices (no hole = no non-default capture). Derived
    /// from <see cref="CaptureModeAt"/> — one source of truth.
    /// </summary>
    public bool IsDefaultModeAt(string template, int i) =>
        CaptureModeAt(template, i) == LogPropertyCaptureMode.Default;

    // ── Resolve-once surface ────────────────────────────────────────────────

    /// <summary>
    /// Resolve the full <see cref="HoleEntry"/> for <paramref name="template"/>
    /// in a single dictionary lookup. Callers that need both the name and the
    /// capture mode for multiple holes should call this once and then read
    /// <see cref="HoleEntry.NameAt"/> / <see cref="HoleEntry.CaptureModeAt"/>
    /// directly — 1 lookup + 2N array reads instead of 2N lookups.
    /// </summary>
    public HoleEntry ResolveEntry(string template) => Resolve(template);

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
        if (string.IsInterned(template) is not null)
        {
            if (_cache.Count < CapacityLimit)
            {
                _cache.TryAdd(template, entry);
            }
            else
            {
                // Interned template that can't be cached because the cache is
                // full: this is the cap-hit. Surface a throttled RuntimeNotice
                // so the silent re-parse-every-dispatch cliff is observable —
                // mirrors NameResolverCache's cap-hit notice.
                PublishCapHitNotice(template);
            }
        }

        return entry;
    }

    // Throttled cap-hit notice on the runtime-notice channel. Multi-tenant hosts
    // that crossed the cap keep seeing the Warning every CapHitNoticeThrottleMs
    // so an operator can surface the template-cardinality issue from a dashboard
    // poll. Allocation is bounded: at most one notice per second per cap-hit
    // burst. Mirrors NameResolverCache.PublishCapHitNotice.
    private void PublishCapHitNotice(string template)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastCapHitNoticeTicks);

        // First miss after a Reset (last == 0) always fires. Subsequent misses
        // fire only after the throttle window has elapsed.
        if (last != 0 && (now - last) < CapHitNoticeThrottleMs)
        {
            return;
        }

        // CAS so a burst of concurrent cap-hits inside the same window produces
        // at most one notice. The loser threads observe the updated last-fired
        // tick on their next call and stay quiet.
        if (Interlocked.CompareExchange(ref _lastCapHitNoticeTicks, now, last) != last)
        {
            return;
        }

        HeraldRuntimeMessages.Publish(
            source: nameof(SerilogTemplateHoleIndex),
            message: $"SerilogTemplateHoleIndex reached its {CapacityLimit}-entry cap; subsequent unique templates will re-parse on every dispatch (most recent miss: template='{template}').",
            severity: NoticeSeverity.Warning);
    }

    private HoleEntry Parse(string template)
    {
        var parsed = _parser.Parse(template);

        // Collect property tokens in declaration order.
        // Cognitive complexity note: single linear pass, no branches inside loop.
        var nameList = new List<string>();
        var modeList = new List<LogPropertyCaptureMode>();

        foreach (var token in parsed.Tokens)
        {
            if (token is MessageTemplateToken.Property prop)
            {
                nameList.Add(prop.Name);
                modeList.Add(prop.CaptureMode);
            }
        }

        return new HoleEntry(nameList.ToArray(), modeList.ToArray());
    }

    // ── Value type for the cache entry ─────────────────────────────────────

    /// <summary>
    /// Frozen arrays for one parsed template. Allocated once per unique
    /// template; reads are lock-free array-index accesses after that.
    ///
    /// <para>
    /// Use <see cref="NameAt"/> and <see cref="CaptureModeAt"/> to read
    /// individual holes. For multi-hole dispatch, obtain an instance via
    /// <see cref="ResolveEntry"/> and call these accessors — they perform
    /// a plain array-index read with no further dictionary lookup.
    /// </para>
    /// </summary>
    public readonly struct HoleEntry
    {
        internal readonly string[] Names;
        internal readonly LogPropertyCaptureMode[] CaptureModes;

        internal HoleEntry(string[] names, LogPropertyCaptureMode[] captureModes)
        {
            Names = names;
            CaptureModes = captureModes;
        }

        /// <summary>
        /// Return the hole name at position <paramref name="i"/>.
        /// Falls back to the positional index string when <paramref name="i"/>
        /// is beyond the template's hole count.
        /// </summary>
        public string NameAt(int i) =>
            i < Names.Length
                ? Names[i]
                : i.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// Return the capture mode at position <paramref name="i"/>.
        /// Returns <see cref="LogPropertyCaptureMode.Default"/> for
        /// out-of-range indices.
        /// </summary>
        public LogPropertyCaptureMode CaptureModeAt(int i) =>
            i < CaptureModes.Length ? CaptureModes[i] : LogPropertyCaptureMode.Default;
    }

    // ── Test / reset surface ────────────────────────────────────────────────

    /// <summary>
    /// Clear the cache and rearm the cap-hit notice throttle. Intended for test
    /// isolation and hot-reload scenarios. Not for production hot-path use.
    /// </summary>
    public void Reset()
    {
        _cache.Clear();
        Interlocked.Exchange(ref _lastCapHitNoticeTicks, 0);
    }

    /// <summary>Current number of cached entries. Diagnostic / test use only.</summary>
    public int Count => _cache.Count;
}
