// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using System.Threading;
using MMP.Herald.Templating;
using MMP.Herald.Templating.NamingPolicies;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Naming-policy surface for <see cref="StructuredLogger"/>. Holds the policy
/// reference, the per-instance diagnostics counters, and the resolver helper
/// that the typed-args generated dispatchers call into.
/// </summary>
public sealed partial class StructuredLogger
{
    // -- diagnostics counters --
    //
    // Resolve/Hit/Miss counters are plain `int` updated with `++` rather than
    // `Interlocked.Increment` — this is the hot path and the diagnostic
    // contract documents saturating semantics on overflow with ±N% drift
    // acceptable. On every platform Herald supports, an aligned 32-bit read
    // and write is atomic at the hardware level, so a reader on another
    // thread never sees a torn value — only a slightly stale one. The total
    // is approximate by design; precise per-call accounting is not the
    // counter's job.
    //
    // FallbackCount uses Add(by N) rather than ++ so it stays on
    // Interlocked.Add (no plain += alternative without losing the atomic
    // contract — and it's a low-frequency counter anyway).
    //
    // CompileTimeResolutions stays on Interlocked.Increment for the same
    // reason as Fallback: it's set from a one-call helper and the cost is
    // negligible against the source-gen-emitted dispatch.
    //
    // CompileTimeResolutions is bumped only by source-gen-emitted dispatchers
    // (Phase 4); runtime dispatchers don't touch it. The diagnostics record
    // reports the split so an operator can tell at a glance whether a service
    // is leaning on source-gen or hand-written typed-args.
    private int _namingResolveCount;
    private int _namingCacheHits;
    private int _namingCacheMisses;
    private long _namingFallbackCount;
    private long _namingCompileTimeResolutions;

    // First-dispatch announcement state (Phase 5).
    //
    // _namingAnnouncementFired: 0 → not yet emitted, 1 → emitted (or
    //   suppressed). Flipped via Interlocked.CompareExchange so exactly
    //   one thread wins the race and emits.
    // _namingAnnouncementSuppressed: set true at construction when the
    //   builder called SuppressNamingPolicyAnnouncement() or the
    //   HERALD_NAMINGPOLICY_QUIET env var is set. When true, the firing
    //   path skips emission but still flips the fired flag so subsequent
    //   dispatches take the fast path.
    private long _namingAnnouncementFired;
    private bool _namingAnnouncementSuppressed;

    // Process-wide env-var probe. Cached once so per-logger construction
    // doesn't repeat the OS call. HERALD_NAMINGPOLICY_QUIET=1 (any
    // non-empty value other than "0" / "false") suppresses the
    // announcement for every logger in the process.
    private static readonly bool s_quietEnvVar = ComputeQuietEnvVar();

    private static bool ComputeQuietEnvVar()
    {
        var raw = System.Environment.GetEnvironmentVariable("HERALD_NAMINGPOLICY_QUIET");
        if (string.IsNullOrEmpty(raw)) return false;
        return !raw.Equals("0", System.StringComparison.Ordinal)
            && !raw.Equals("false", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The naming policy this logger applies when resolving property names
    /// from typed-args call shapes. Set at construction (or post-bootstrap
    /// via <see cref="InstallNamingPolicy"/>); child loggers produced by
    /// <see cref="WithContext"/> inherit the policy by reference at scope
    /// creation — a parent rebuilt with a different policy does NOT
    /// retroactively flip a child.
    ///
    /// <para>
    /// Volatile-read so concurrent install + read on different threads stay
    /// consistent. When no explicit policy is installed, the getter resolves
    /// to <see cref="PascalCasePolicy.Instance"/> — that's the spec default
    /// and what the public surface has always returned.
    /// </para>
    /// </summary>
    public IPropertyNamingPolicy NamingPolicy =>
        Volatile.Read(ref _namingPolicy) ?? PascalCasePolicy.Instance;

    /// <summary>
    /// The kind of naming policy currently installed. Used by interceptor-emitted
    /// dispatch code to pick the right baked-name lane without policy-type
    /// reflection. <see cref="BuiltinPolicy.Pascal"/> is the default (also
    /// returned when no policy is installed). <see cref="BuiltinPolicy.Custom"/>
    /// indicates a non-built-in policy is in use; emitted interceptors fall
    /// through to the runtime resolver in that case.
    /// </summary>
    public BuiltinPolicy CurrentPolicyKind => _currentPolicyKind;

    /// <summary>
    /// Atomically install a property-naming policy. Called once at bootstrap
    /// when <see cref="MMP.Herald.Quick.QuickLogBuilder.WithNamingPolicy"/>
    /// is configured; hot-reload integration can re-install with a fresh
    /// policy reference. Passing <c>null</c> resets the logger to the spec
    /// default (Pascal) — the public <see cref="NamingPolicy"/> getter
    /// continues to report PascalCasePolicy.Instance to keep the contract
    /// stable.
    /// </summary>
    internal void InstallNamingPolicy(IPropertyNamingPolicy? policy)
    {
        Interlocked.Exchange(ref _namingPolicy, policy);
        // Companion field stays in lockstep so the interceptor's
        // BuiltinPolicy switch (P3) sees the new kind on the next dispatch.
        // Plain assignment: an aligned 4-byte enum write is atomic on every
        // platform Herald supports; a reader on another thread sees either
        // the old or new kind, never a torn intermediate.
        _currentPolicyKind = ClassifyPolicyKind(policy);
    }

    /// <summary>
    /// Classify an <see cref="IPropertyNamingPolicy"/> reference into a
    /// <see cref="BuiltinPolicy"/> kind. Null and PascalCasePolicy.Instance
    /// both collapse to <see cref="BuiltinPolicy.Pascal"/>. Anything outside
    /// the built-in set lands on <see cref="BuiltinPolicy.Custom"/>.
    /// </summary>
    internal static BuiltinPolicy ClassifyPolicyKind(IPropertyNamingPolicy? policy) => policy switch
    {
        null => BuiltinPolicy.Pascal,
        PascalCasePolicy => BuiltinPolicy.Pascal,
        SnakeCasePolicy => BuiltinPolicy.Snake,
        CamelCasePolicy => BuiltinPolicy.Camel,
        _ => BuiltinPolicy.Custom,
    };

    /// <summary>
    /// Snapshot of the per-logger naming-policy diagnostics. Counters are
    /// monotonic for the lifetime of the <see cref="StructuredLogger"/>
    /// instance.
    ///
    /// <para>
    /// The Resolve/Hit/Miss counters use plain-int storage with drift-tolerant
    /// semantics on the hot path (see field-level note in this file). The
    /// snapshot widens int -> long so the public record's <c>long</c> shape
    /// stays stable; an int counter that saturates at int.MaxValue surfaces
    /// here as a long that stopped advancing.
    /// </para>
    /// </summary>
    public NamingPolicyDiagnostics GetNamingPolicyDiagnostics()
    {
        return new NamingPolicyDiagnostics(
            PolicyId: NamingPolicy.Id,
            ResolutionCount: Volatile.Read(ref _namingResolveCount),
            CacheHits: Volatile.Read(ref _namingCacheHits),
            CacheMisses: Volatile.Read(ref _namingCacheMisses),
            FallbackCount: Volatile.Read(ref _namingFallbackCount),
            CompileTimeResolutions: Volatile.Read(ref _namingCompileTimeResolutions));
    }

    /// <summary>
    /// Resolve the per-slot property names for a single typed-args call.
    /// Called by the source-generator-emitted <c>DispatchTypedN</c> path
    /// (Phase 4) for hand-written <c>logger.Info(template, args)</c> calls.
    /// The result is a cache-owned <c>string[]</c> — callers must treat as
    /// read-only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hot-path contract:
    /// <list type="number">
    ///   <item>On cache hit: one <c>ConcurrentDictionary.TryGetValue</c> + two
    ///         <c>Interlocked.Increment</c> calls + return the cached
    ///         reference. Zero allocation.</item>
    ///   <item>On cache miss: parse + policy invocation + cache insert. Hot
    ///         only on first dispatch per unique template; amortised to the
    ///         hit path on every call after.</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <summary>
    /// Cache-hit fast path used by the source-generated typed-args
    /// dispatchers. Returns the cached resolved-name array when the
    /// (policy, template) pair has been resolved before; null otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Generator-emitted dispatchers call this first; only on null do they
    /// allocate the params-array for <see cref="ResolveAndCacheNames"/>.
    /// The result is zero-allocation on every cache hit — once a template
    /// has been seen, every subsequent dispatch through it pays exactly one
    /// dictionary lookup + two Interlocked.Increment calls and reuses the
    /// cached <c>string[]</c> by reference.
    /// </para>
    /// </remarks>
    internal string[]? TryGetCachedNames(string template)
    {
        EnsureAnnouncementFired();
        var policy = Volatile.Read(ref _namingPolicy) ?? PascalCasePolicy.Instance;
        if (NameResolverCache.TryGetCached(policy, template, out var cached) && cached is not null)
        {
            // Plain ++ — these counters are drift-tolerant by design.
            // 32-bit reads/writes are atomic at the hardware level on every
            // platform Herald supports, so a concurrent reader sees a stale
            // count but never a torn value.
            _namingResolveCount++;
            _namingCacheHits++;
            return cached;
        }
        return null;
    }

    /// <summary>
    /// Cold-miss path used by the source-generated typed-args dispatchers.
    /// Parses the template, runs the active policy, caches the result, and
    /// returns it. The <c>params</c> array is constructed by the caller's
    /// compiler — accepted as a per-template one-shot allocation rather
    /// than a per-call cost (every subsequent dispatch hits
    /// <see cref="TryGetCachedNames"/> instead).
    /// </summary>
    internal string[] ResolveAndCacheNames(string template, params string?[] argExprs)
    {
        var policy = Volatile.Read(ref _namingPolicy) ?? PascalCasePolicy.Instance;

        // Normalize null entries to empty strings so the policy's
        // fallback chain (which checks IsNullOrEmpty) treats them
        // consistently. Modifying the array in place is safe — the array
        // is allocated for this call and not retained anywhere.
        for (var i = 0; i < argExprs.Length; i++)
        {
            argExprs[i] ??= string.Empty;
        }

        // Cast nullable-string array to non-nullable span: the runtime
        // representation is the same (string[] vs string?[] differ only in
        // nullable annotation metadata) and we just normalized any null
        // entries to empty above.
        var names = NameResolverCache.Resolve(policy, template, argExprs!);

        _namingResolveCount++;
        _namingCacheMisses++;

        return names;
    }

    /// <summary>
    /// Resolve per-slot property names for a single typed-args call. The
    /// generator emits calls to this overload — it passes the raw template
    /// string + the caller-argument-expression names and lets the resolver
    /// cache parse + cache the template internally.
    /// </summary>
    /// <remarks>
    /// Hot-path contract is identical to the tokens-supplied overload below:
    /// cache hit → one TryGetValue + counter bumps + return cached reference,
    /// zero allocation. Miss → parse template, run policy, cache, return.
    /// </remarks>
    internal string[] ResolveNames(
        string template,
        ReadOnlySpan<string> argExprs)
    {
        var policy = Volatile.Read(ref _namingPolicy) ?? PascalCasePolicy.Instance;
        var names = NameResolverCache.Resolve(
            policy, template, argExprs, out var wasCacheHit);

        _namingResolveCount++;
        if (wasCacheHit) _namingCacheHits++; else _namingCacheMisses++;

        return names;
    }

    /// <summary>
    /// Tokens-supplied overload. Used by tests / advanced callers that want
    /// to bypass the parser hop (e.g. policy-unit tests). The fallback-count
    /// path is identical to the no-token overload.
    /// </summary>
    internal string[] ResolveNames(
        string template,
        ReadOnlySpan<MessageTemplateToken.Property> tokens,
        ReadOnlySpan<string> argExprs)
    {
        var policy = Volatile.Read(ref _namingPolicy) ?? PascalCasePolicy.Instance;
        var names = NameResolverCache.Resolve(
            policy, template, tokens, argExprs, out var wasCacheHit);

        _namingResolveCount++;
        if (wasCacheHit) _namingCacheHits++; else _namingCacheMisses++;

        // Fallback count: slots that had no corresponding template token, so
        // the policy used the caller-argument-expression name instead. A
        // per-call sum is the cheapest accurate measure — we don't inspect
        // the resolved names themselves (that would force a per-slot string
        // compare). When tokens.Length >= argExprs.Length there are no
        // fallbacks for this call.
        if (tokens.Length < argExprs.Length)
        {
            Interlocked.Add(ref _namingFallbackCount, argExprs.Length - tokens.Length);
        }

        return names;
    }

    /// <summary>
    /// Increment the compile-time-resolutions counter. Called by source-gen-
    /// emitted dispatchers (Phase 4 <c>[HeraldLog]</c> path) whose property
    /// names are baked into IL at compile time and therefore never touch the
    /// runtime cache. Surfaces in <see cref="GetNamingPolicyDiagnostics"/>
    /// so an operator can see how much of the load is on the source-gen path.
    /// </summary>
    internal void RecordCompileTimeResolution()
    {
        Interlocked.Increment(ref _namingCompileTimeResolutions);
        EnsureAnnouncementFired();
    }

    /// <summary>
    /// Bookkeeping hook called by every generator-emitted interceptor body.
    /// Arms the first-dispatch announcement gate and bumps the
    /// compile-time-resolutions counter so an operator inspecting
    /// <see cref="GetNamingPolicyDiagnostics"/> sees that the consumer is
    /// dispatching through baked interceptors instead of the runtime resolver.
    ///
    /// <para>
    /// Public so the interceptor — which lives in the consumer assembly's
    /// <c>MMP.Herald.Generated</c> namespace — can call into Herald.OSS across
    /// the assembly boundary. The method is otherwise a thin wrapper over
    /// <see cref="RecordCompileTimeResolution"/>; the only reason it has its
    /// own name is so a future split between source-gen counter and
    /// interceptor counter is a one-call-site change at the generator.
    /// </para>
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void RecordInterceptedDispatch()
    {
        Interlocked.Increment(ref _namingCompileTimeResolutions);
        EnsureAnnouncementFired();
    }

    /// <summary>
    /// First-dispatch announcement gate (Phase 5). Called from every
    /// runtime + source-gen dispatch entry. After the first invocation,
    /// further calls are a single <see cref="Volatile.Read"/> + early
    /// return — ~1 ns of hot-path cost.
    /// </summary>
    /// <remarks>
    /// The CompareExchange flips the flag synchronously so subsequent
    /// dispatches take the fast path immediately. The publish itself is
    /// queued to the thread pool so the first emitter does not pay the
    /// announcement's runtime-message cost on its hot path. A consumer who
    /// races a dispatch immediately after build sees the publish a moment
    /// later; the announcement's purpose is operator-visible diagnostics,
    /// not per-event ordering.
    /// </remarks>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal void EnsureAnnouncementFired()
    {
        if (Volatile.Read(ref _namingAnnouncementFired) != 0) return;
        if (Interlocked.CompareExchange(ref _namingAnnouncementFired, 1, 0) != 0) return;

        // Suppression check before queueing — no point scheduling work just
        // to drop it. Suppressed loggers pay nothing past the CompareExchange.
        if (_namingAnnouncementSuppressed || s_quietEnvVar) return;

        // ThreadPool.UnsafeQueueUserWorkItem<T>(state) avoids the ExecutionContext
        // capture overhead. The publish runs off-thread; the calling dispatch
        // returns immediately.
        System.Threading.ThreadPool.UnsafeQueueUserWorkItem(
            static state => state.FireAnnouncement(), this, preferLocal: false);
    }

    private void FireAnnouncement()
    {
        // Publish to the runtime-message channel, NOT through the
        // logger's own pipeline. Routing a framework signal through the
        // user pipeline breaks the wall the consumer built: per-tenant
        // sinks would end up with framework events the application
        // never logged. The runtime-message channel is process-wide,
        // separate from any pipeline, and intended for diagnostic
        // surfaces (a debug console, a startup log scraper, an admin
        // dashboard). A consumer who wants the announcement on their
        // pipeline subscribes to HeraldRuntimeMessages.OnNotice and
        // re-publishes — the choice is theirs, not the framework's.
        var policy = NamingPolicy;
        Diagnostics.HeraldRuntimeMessages.Publish(
            source: "@herald.runtime.naming-policy",
            message:
                $"Herald active naming policy: {policy.Id}. " +
                "Call .WithNamingPolicy(PropertyNamingPolicy.Camel | .Snake | .Pascal) " +
                "on the builder to change it. " +
                "To silence this message call .SuppressNamingPolicyAnnouncement() on the builder " +
                "or set HERALD_NAMINGPOLICY_QUIET=1.",
            properties: new[] { new Templating.LogProperty("PolicyId", policy.Id) });
    }

    /// <summary>
    /// Suppress the first-dispatch announcement on this logger. Called by
    /// <see cref="MMP.Herald.Quick.QuickLogBuilder.SuppressNamingPolicyAnnouncement"/>
    /// at build time, before any dispatch.
    /// </summary>
    internal void SuppressAnnouncement()
    {
        _namingAnnouncementSuppressed = true;
    }
}
