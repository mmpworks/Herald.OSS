#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using MMP.Herald.Pipeline.Kernel;

namespace MMP.Herald.Routing.Map;

/// <summary>
/// Fluent builder for a <see cref="MappedKernelSink"/> route table. Collects
/// closed (enumerated-at-build-time) routes, an optional default, and — when
/// the caller opts into open-key routing — the cardinality cap, overflow
/// policy, and lazy factory.
///
/// <para>
/// Two shapes come out of this builder:
/// </para>
/// <list type="bullet">
///   <item><b>Closed-key</b> — only <see cref="Add"/> / <see cref="Default"/>
///   called. Routes freeze into a <see cref="FrozenDictionary{TKey,TValue}"/>;
///   0-alloc lookup on every runtime; no factory, no cap, no injection
///   surface. This is the recommended shape.</item>
///   <item><b>Open-key</b> — <see cref="WithFactory"/> called. Unseen keys
///   auto-create a route through the factory, bounded by the cap, with
///   overflow routed per <see cref="RouteOverflowPolicy"/>. The caller
///   explicitly accepts cardinality ownership by calling this method.</item>
/// </list>
/// </summary>
public sealed class MapRouteBuilder
{
    private readonly Dictionary<string, IKernelSink> _routes =
        new(StringComparer.Ordinal);
    private IKernelSink? _default;
    private Func<string, IKernelSink>? _factory;
    private RouteOverflowPolicy _overflowPolicy = RouteOverflowPolicy.Drop;
    private int _maxDynamicRoutes = MappedKernelSink.DefaultMaxDynamicRoutes;
    private int _maxKeyLength = RouteKey.DefaultMaxKeyLength;

    /// <summary>Add a closed route: events whose key equals <paramref name="key"/> go to <paramref name="sink"/>.</summary>
    public MapRouteBuilder Add(string key, IKernelSink sink)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(sink);
        _routes[key] = sink;
        return this;
    }

    /// <summary>
    /// Set the default sink: where events go when their key matches no route,
    /// is invalid, or (under <see cref="RouteOverflowPolicy.RouteToDefault"/>)
    /// overflows the cap. Optional — without a default, unmatched events are
    /// dropped.
    /// </summary>
    public MapRouteBuilder Default(IKernelSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _default = sink;
        return this;
    }

    /// <summary>
    /// Opt into open-key routing. Unseen valid keys auto-create a route via
    /// <paramref name="factory"/>, up to <paramref name="maxDynamicRoutes"/>
    /// distinct keys; further keys follow <paramref name="overflowPolicy"/>.
    ///
    /// <para>
    /// Calling this method is the explicit "I own the cardinality" contract.
    /// Created routes are never evicted — eviction would race the factory and
    /// risk truncating a half-written audit file — so the cap is the only
    /// bound on resource use. Size it to the real distinct-key count you
    /// expect, not the theoretical maximum.
    /// </para>
    /// </summary>
    public MapRouteBuilder WithFactory(
        Func<string, IKernelSink> factory,
        int maxDynamicRoutes = MappedKernelSink.DefaultMaxDynamicRoutes,
        RouteOverflowPolicy overflowPolicy = RouteOverflowPolicy.Drop,
        int maxKeyLength = RouteKey.DefaultMaxKeyLength)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (maxDynamicRoutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDynamicRoutes),
                "Open-key routing requires a positive cardinality cap.");
        if (maxKeyLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxKeyLength),
                "Key length cap must be positive.");

        _factory = factory;
        _maxDynamicRoutes = maxDynamicRoutes;
        _overflowPolicy = overflowPolicy;
        _maxKeyLength = maxKeyLength;
        return this;
    }

    /// <summary>Materialise the configured route table into a <see cref="MappedKernelSink"/>.</summary>
    public MappedKernelSink Build(LogEventBufferKeySelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var frozen = _routes.ToFrozenDictionary(StringComparer.Ordinal);
        return new MappedKernelSink(
            selector, frozen, _default, _factory,
            _maxDynamicRoutes, _overflowPolicy, _maxKeyLength);
    }
}
