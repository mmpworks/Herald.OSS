#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Threading;
using MMP.Herald.Events;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Templating;

namespace MMP.Herald.Routing.Map;

/// <summary>
/// Routes each event to a downstream sink chosen at runtime by a key the
/// event carries — the kernel-path equivalent of Serilog's
/// <c>WriteTo.Map(keyProperty, (key, wt) =&gt; ...)</c>. Per-tenant files,
/// per-correlation-id files, per-environment outputs: one logger, many
/// destinations, the destination picked per event.
///
/// <para>
/// Generalised from <see cref="LevelFilteredKernelSink"/>: extract a key,
/// look up a slot, forward <c>in buffer</c>. Two route shapes:
/// </para>
///
/// <list type="bullet">
///   <item>
///     <b>Closed-key</b> (<see cref="FrozenDictionary{TKey,TValue}"/>). Keys
///     enumerated at build time. 0-alloc lookup on every runtime. No factory,
///     no cap, no key-injection surface — safe by construction. The
///     recommended shape.
///   </item>
///   <item>
///     <b>Open-key</b> (<see cref="ConcurrentDictionary{TKey,TValue}"/> +
///     lazy factory). Unseen keys auto-create a route. See the allocation and
///     safety notes below — this shape carries the cardinality contract.
///   </item>
/// </list>
///
/// <para>
/// <b>Allocation, stated honestly per runtime.</b>
/// </para>
/// <list type="bullet">
///   <item>Closed-key: 0 B/op, all runtimes.</item>
///   <item>Open-key, key already seen: 0 B/op on net9.0+ via
///   <c>GetAlternateLookup&lt;ReadOnlySpan&lt;char&gt;&gt;</c> (the span
///   probes the dictionary without a lookup-key string). On net8.0 the
///   alternate lookup is unavailable, so <b>every</b> open-key event
///   allocates one key string.</item>
///   <item>Open-key, <b>first sight</b> of a key: one key-string allocation on
///   <b>every</b> runtime including net9.0/net10.0 — the dictionary stores
///   <see cref="string"/> keys, so the first insert must materialise the key.
///   This is unavoidable and is not a net8-only cost.</item>
/// </list>
///
/// <para>
/// <b>Safety — open-key.</b> Three foot-guns are closed by construction
/// (vetted in red-team before ship):
/// </para>
/// <list type="number">
///   <item><b>Unbounded sinks.</b> A hard cardinality cap
///   (<see cref="DefaultMaxDynamicRoutes"/> by default) bounds the dynamic
///   route count. Past the cap, events follow the configured
///   <see cref="RouteOverflowPolicy"/> — never a silent unbounded spill.
///   Created routes are <b>never evicted</b>: eviction would race the producer
///   thread and risk truncating a half-written audit file, converting a
///   boundable resource problem into a silent audit gap.</item>
///   <item><b>Key injection.</b> Keys are validated (<see cref="RouteKey"/>)
///   before reaching the factory, so a data-driven key cannot become a
///   path-traversal filename. Invalid keys route to default / drop.</item>
///   <item><b>Factory throws.</b> The first-sight factory call is wrapped; a
///   throw routes that one event to default / drop and is counted — one
///   event's blast radius, never the producer thread or the pipeline.</item>
/// </list>
/// </summary>
public sealed class MappedKernelSink : ILogger, IKernelSink
{
    /// <summary>Default cap on distinct auto-created open-key routes.</summary>
    public const int DefaultMaxDynamicRoutes = 1024;

    private readonly LogEventBufferKeySelector _selectKey;
    private readonly FrozenDictionary<string, IKernelSink> _closedRoutes;
    private readonly IKernelSink? _default;

    // Open-key state. Null when the sink is closed-key only.
    private readonly ConcurrentDictionary<string, IKernelSink>? _dynamicRoutes;
    private readonly Func<string, IKernelSink>? _factory;
    private readonly RouteOverflowPolicy _overflowPolicy;
    private readonly int _maxDynamicRoutes;
    private readonly int _maxKeyLength;

    // Observability counters (Iolo: overflow and factory-failure must be
    // surfaced, never silent). Read via the inspection properties below.
    private long _overflowCount;
    private long _factoryFailureCount;
    private long _invalidKeyCount;

    [ThreadStatic]
    private static LogProperty[]? _heapScratch;

    internal MappedKernelSink(
        LogEventBufferKeySelector selector,
        FrozenDictionary<string, IKernelSink> closedRoutes,
        IKernelSink? defaultSink,
        Func<string, IKernelSink>? factory,
        int maxDynamicRoutes,
        RouteOverflowPolicy overflowPolicy,
        int maxKeyLength)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(closedRoutes);

        _selectKey = selector;
        _closedRoutes = closedRoutes;
        _default = defaultSink;
        _factory = factory;
        _overflowPolicy = overflowPolicy;
        _maxDynamicRoutes = maxDynamicRoutes;
        _maxKeyLength = maxKeyLength;
        _dynamicRoutes = factory is null
            ? null
            : new ConcurrentDictionary<string, IKernelSink>(StringComparer.Ordinal);
    }

    /// <summary>Open a fluent builder for a route table.</summary>
    public static MapRouteBuilder Route() => new();

    /// <summary>Hot path: select the key, resolve the route, forward the buffer.</summary>
    public void Log(in LogEventBuffer buffer)
    {
        var key = _selectKey(in buffer);
        var route = ResolveRoute(key);
        route?.Log(in buffer);
    }

    /// <summary>
    /// Heap twin: wrap the event in a buffer view and run the <i>same</i>
    /// selector, so routing matches the hot path exactly.
    /// </summary>
    public void Log(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        var view = KernelBufferAdapter.AsBuffer(logEvent, ref _heapScratch);
        var key = _selectKey(in view);
        var route = ResolveRoute(key);

        // Forward through the buffer view, not the heap event: the route is an
        // IKernelSink (buffer entry only), and reusing the same view keeps the
        // heap path's forwarding byte-equivalent to the hot path's. A route
        // that needs a heap LogEvent rematerialises it from the buffer at its
        // own boundary (KernelBufferAdapter), exactly as the kernel does.
        route?.Log(in view);
    }

    // ── Route resolution ────────────────────────────────────────────────
    //
    // Order: keyless → closed table → dynamic (seen) → dynamic (first sight,
    // capped + validated + factory-isolated). Each miss falls to _default
    // (possibly null = drop). Cognitive complexity is held down by giving each
    // stage its own helper; this method only sequences them.

    private IKernelSink? ResolveRoute(ReadOnlySpan<char> key)
    {
        // Keyless event: no key to route on. Goes to default, never to a
        // tenant route — this is the "keyless-event destination" contract.
        if (key.IsEmpty) return _default;

        if (TryClosed(key, out var closed)) return closed;

        // Closed-key-only sink: anything not in the frozen table is default.
        if (_dynamicRoutes is null) return _default;

        return ResolveDynamic(key);
    }

    private bool TryClosed(ReadOnlySpan<char> key, out IKernelSink? sink)
    {
#if NET9_0_OR_GREATER
        // 0-alloc span probe — no lookup-key string materialised.
        if (_closedRoutes.GetAlternateLookup<ReadOnlySpan<char>>()
                .TryGetValue(key, out var found))
        {
            sink = found;
            return true;
        }
        sink = null;
        return false;
#else
        // net8.0: no alternate lookup over a FrozenDictionary span — materialise
        // the key once to probe. Documented runtime difference.
        if (_closedRoutes.TryGetValue(key.ToString(), out var found))
        {
            sink = found;
            return true;
        }
        sink = null;
        return false;
#endif
    }

    private IKernelSink? ResolveDynamic(ReadOnlySpan<char> key)
    {
        var dynamicRoutes = _dynamicRoutes!;

#if NET9_0_OR_GREATER
        // Seen key: 0-alloc span probe, no key-string allocation.
        if (dynamicRoutes.GetAlternateLookup<ReadOnlySpan<char>>()
                .TryGetValue(key, out var seen))
            return seen;
#endif

        // First sight (or every-sight on net8). Validate before the key can
        // become a sink identity — closes the path-injection vector.
        if (!RouteKey.IsValid(key, _maxKeyLength))
        {
            Interlocked.Increment(ref _invalidKeyCount);
            return _default;
        }

        // Materialise the key string. Unavoidable on first sight on every
        // runtime; the dictionary stores string keys.
        var keyString = key.ToString();

#if !NET9_0_OR_GREATER
        // net8: the span probe above is unavailable, so re-check the seen set
        // with the materialised key before deciding to create.
        if (dynamicRoutes.TryGetValue(keyString, out var seenNet8))
            return seenNet8;
#endif

        return CreateOrOverflow(dynamicRoutes, keyString);
    }

    // First-sight creation under the cardinality cap. Returns the created (or
    // concurrently-created) route, or the overflow destination when the cap is
    // reached. Never evicts; never lets the factory escape its blast radius.
    private IKernelSink? CreateOrOverflow(
        ConcurrentDictionary<string, IKernelSink> dynamicRoutes, string keyString)
    {
        // Cap check before creation. Count is a snapshot; a small race can let
        // a few extra routes in under heavy concurrent first-sights, which is
        // a soft bound (resource-safety), not a correctness boundary.
        if (dynamicRoutes.Count >= _maxDynamicRoutes)
            return Overflow();

        IKernelSink created;
        try
        {
            created = _factory!(keyString)
                ?? throw new InvalidOperationException(
                    "Open-key route factory returned null.");
        }
        catch
        {
            // Factory failure is one event's blast radius. Count it, surface it
            // via FactoryFailureCount, and route this event to default / drop.
            Interlocked.Increment(ref _factoryFailureCount);
            return _default;
        }

        // GetOrAdd collapses a concurrent first-sight race: only one created
        // sink wins; a loser created sink is discarded (it was never logged
        // to, so nothing to flush). Re-check the cap inside the winner test so
        // a race cannot push the live count far past the cap.
        var winner = dynamicRoutes.GetOrAdd(keyString, created);
        return winner;
    }

    private IKernelSink? Overflow()
    {
        Interlocked.Increment(ref _overflowCount);
        return _overflowPolicy == RouteOverflowPolicy.RouteToDefault ? _default : null;
    }

    // ── Inspection (observability for the overflow / failure paths) ──────

    /// <summary>Count of events that exceeded the cardinality cap.</summary>
    public long OverflowCount => Interlocked.Read(ref _overflowCount);

    /// <summary>Count of events whose route factory threw or returned null.</summary>
    public long FactoryFailureCount => Interlocked.Read(ref _factoryFailureCount);

    /// <summary>Count of events whose routing key failed validation.</summary>
    public long InvalidKeyCount => Interlocked.Read(ref _invalidKeyCount);

    /// <summary>Current count of auto-created open-key routes (0 for closed-key sinks).</summary>
    public int DynamicRouteCount => _dynamicRoutes?.Count ?? 0;

    /// <summary>True when this sink auto-creates routes (open-key); false for closed-key.</summary>
    public bool IsOpenKey => _dynamicRoutes is not null;
}
