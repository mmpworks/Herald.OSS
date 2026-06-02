#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MMP.Herald.Events;
using MMP.Herald.Serilog.Configuration;

namespace MMP.Herald.Serilog.Sinks;

/// <summary>
/// A secondary destination on the Serilog side that is itself a full
/// sub-pipeline (a child <see cref="LoggerConfiguration"/>). Backs
/// <c>WriteTo.Logger(...)</c> and each per-key branch of <c>WriteTo.Map(...)</c>.
///
/// <para>
/// A route owns its child pipeline lifetime: <see cref="DisposeAsync"/> flushes
/// and releases the child when the parent pipeline closes. Routes are built and
/// disposed by <see cref="SerilogSinkAdapter"/>, which is the single owner of the
/// Serilog null-slot, so the parent's CloseAndFlush drains every route exactly once.
/// </para>
/// </summary>
internal interface ISubLoggerRoute : IAsyncDisposable
{
    /// <summary>Route one already-finalised native event into this destination.</summary>
    void Accept(LogEvent logEvent);
}

/// <summary>
/// A single fixed sub-logger destination — the <c>WriteTo.Logger(lc =&gt; ...)</c>
/// case. The child pipeline is built eagerly when the route is created (config
/// time) and every event is forwarded straight into it.
/// </summary>
internal sealed class FixedSubLoggerRoute : ISubLoggerRoute
{
    private readonly SerilogLoggerAdapter _child;

    private FixedSubLoggerRoute(SerilogLoggerAdapter child) => _child = child;

    /// <summary>
    /// Build the child pipeline from <paramref name="configure"/> and wrap it as
    /// a route. The child owns its pipeline (CreateLogger returns a FromBuild
    /// adapter), so this route can flush it on dispose.
    /// </summary>
    internal static FixedSubLoggerRoute Build(Action<LoggerConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var childConfig = new LoggerConfiguration();
        configure(childConfig);
        var child = (SerilogLoggerAdapter)childConfig.CreateLogger();
        return new FixedSubLoggerRoute(child);
    }

    // Forward the fully-enriched event straight into the child pipeline. We feed
    // the native event so the child re-runs its own filters/sinks but NOT a
    // second enrichment pass — the event is already finalised.
    // Internal forward: Herald re-feeding an event it built into the child
    // pipeline is NOT external injection, so it bypasses the consent gate via
    // ForwardPrebuilt rather than the public Log(LogEvent) port.
    public void Accept(LogEvent logEvent) => _child.HeraldLogger.ForwardPrebuilt(logEvent);

    public ValueTask DisposeAsync() => _child.DisposeAsync();
}

/// <summary>
/// The <c>WriteTo.Map&lt;TKey&gt;(...)</c> case: dynamic per-event routing. For
/// each event a key is computed; the first time a key is seen its sub-logger is
/// built lazily via the user <c>configure(key, sinkConfig)</c> callback and
/// cached. Subsequent events with that key reuse the cached sub-logger.
///
/// <para>
/// <b>Count limit.</b> At most <c>sinkMapCountLimit</c> distinct sub-loggers are
/// kept. When the limit is reached and a new key arrives, the least-recently
/// created sub-logger is evicted (flushed + disposed) to make room — bounding
/// the resource footprint exactly as Serilog.Sinks.Map does.
/// </para>
///
/// <para>
/// <b>Default key.</b> When the key property is absent (or its value is null)
/// the configured default key is used; events still route somewhere rather than
/// being dropped.
/// </para>
/// </summary>
/// <typeparam name="TKey">The routing key type.</typeparam>
internal sealed class MapSubLoggerRoute<TKey> : ISubLoggerRoute
    where TKey : notnull
{
    private readonly Func<LogEvent, TKey> _keySelector;
    private readonly Action<TKey, LoggerSinkConfiguration> _configure;
    private readonly int _countLimit;

    // Insertion-ordered cache: the Dictionary holds the live sub-loggers; the
    // List preserves creation order so eviction is deterministic (oldest first).
    // Both are guarded by _gate — events arrive on the drain thread, but a
    // misconfigured pipeline could deliver concurrently, so the lazy build and
    // the eviction must be atomic relative to each other.
    private readonly Dictionary<TKey, SerilogLoggerAdapter> _children = new();
    private readonly List<TKey> _order = new();
    private readonly object _gate = new();
    private bool _disposed;

    internal MapSubLoggerRoute(
        Func<LogEvent, TKey> keySelector,
        Action<TKey, LoggerSinkConfiguration> configure,
        int countLimit)
    {
        _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
        _configure = configure ?? throw new ArgumentNullException(nameof(configure));
        // A count limit below 1 would route nothing; clamp to at least one live
        // sub-logger so the route always has somewhere to send the current event.
        _countLimit = countLimit < 1 ? 1 : countLimit;
    }

    public void Accept(LogEvent logEvent)
    {
        var key = _keySelector(logEvent);

        SerilogLoggerAdapter child;
        SerilogLoggerAdapter? evicted = null;

        lock (_gate)
        {
            if (_disposed) return;

            if (!_children.TryGetValue(key, out var existing))
            {
                // Make room first so we never exceed the limit even transiently.
                if (_children.Count >= _countLimit && _order.Count > 0)
                    evicted = EvictOldest();

                existing = BuildChild(key);
                _children[key] = existing;
                _order.Add(key);
            }

            child = existing;
        }

        // Dispose the evicted sub-logger OUTSIDE the lock — flushing its buffer
        // can block, and we must not hold _gate across an I/O drain.
        evicted?.DisposeAsync().AsTask().GetAwaiter().GetResult();

        // Internal forward (see Accept above): bypass the consent gate.
        child.HeraldLogger.ForwardPrebuilt(logEvent);
    }

    // Build a sub-logger for one key by running the user configure callback
    // against a fresh child LoggerConfiguration's WriteTo accessor.
    private SerilogLoggerAdapter BuildChild(TKey key)
    {
        var childConfig = new LoggerConfiguration();
        _configure(key, childConfig.WriteTo);
        return (SerilogLoggerAdapter)childConfig.CreateLogger();
    }

    // Remove and return the oldest live sub-logger. Caller disposes it outside
    // the lock. Single linear step — no recursion, no nested branching.
    private SerilogLoggerAdapter EvictOldest()
    {
        var oldestKey = _order[0];
        _order.RemoveAt(0);
        var oldest = _children[oldestKey];
        _children.Remove(oldestKey);
        return oldest;
    }

    public async ValueTask DisposeAsync()
    {
        List<SerilogLoggerAdapter> toDispose;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            toDispose = new List<SerilogLoggerAdapter>(_children.Values);
            _children.Clear();
            _order.Clear();
        }

        foreach (var child in toDispose)
            await child.DisposeAsync().ConfigureAwait(false);
    }
}
