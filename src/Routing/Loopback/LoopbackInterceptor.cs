#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Pipeline.Kernel;

namespace MMP.Herald.Routing.Loopback;

/// <summary>
/// Wraps a real <see cref="ILogger"/> sink with the disabled / live /
/// test runState gate, the per-sink minimum-level gate, and the
/// optional file + URL + bus loopback legs. The interceptor is the
/// single point where every event for the wrapped sink passes
/// through; flipping the <see cref="SinkRunStateHolder"/> or the
/// <see cref="SinkOverridesHolder"/> changes the very next event's
/// behaviour without rebuilding the pipeline.
///
/// <para>Per-event order on <see cref="Log"/>:</para>
/// <list type="number">
///   <item><b>Disabled drop</b> — earliest exit. One Volatile read on
///         the runState holder, one branch. Cheapest path; the wrapped
///         sink never sees the event.</item>
///   <item><b>Per-sink minLevel drop</b> — second exit. Null gate
///         skips the lookup; otherwise the registry's cached
///         <c>IsBelow</c> answers in two dictionary hits. Events
///         below the gate drop before the sink call so we don't
///         pay rendering cost on filtered events.</item>
///   <item><b>Live / Test routing</b> — Live forwards to the inner
///         sink first so a loopback failure can't suppress the real
///         send; Test suppresses the inner sink and routes only to
///         the loopback legs.</item>
///   <item><b>Tee dispatch</b> — file / URL / bus legs fire per the
///         tee flags in the overrides holder. Test mode unconditionally
///         uses every leg; Live consults the flags. The bus is the
///         dashboard's always-on observer of test traffic.</item>
/// </list>
/// </summary>
public class LoopbackInterceptor : ILogger, IDisposable
{
    // ── Wrap factory ─────────────────────────────────────────────
    // Picks the kernel-aware variant when the inner sink implements
    // IKernelSink so the kernel fast-path stays alive on live mode +
    // no tee. Otherwise the plain ILogger variant.
    public static LoopbackInterceptor Wrap(
        ILogger innerSink,
        SinkRunStateHolder state,
        SinkOverridesHolder overrides,
        ILogLevelRegistry levels,
        string pipelineName,
        string sinkName,
        LoopbackFileWriter? file,
        LoopbackUrlPoster? url)
    {
        if (innerSink is IKernelSink kernel)
        {
            return new LoopbackInterceptorKernel(
                innerSink, kernel, state, overrides, levels, pipelineName, sinkName, file, url);
        }
        return new LoopbackInterceptor(
            innerSink, state, overrides, levels, pipelineName, sinkName, file, url);
    }

    protected readonly ILogger _innerSink;
    protected readonly SinkRunStateHolder _state;
    protected readonly SinkOverridesHolder _overrides;
    protected readonly ILogLevelRegistry _levels;
    private readonly LoopbackFileWriter? _file;
    // _url is protected so the kernel subclass can probe it on the
    // hot path (only materialise the rejection LogEvent when a URL
    // leg is wired — most production builds have no URL leg, so the
    // probe + skip keeps the kernel allocation-free).
    protected readonly LoopbackUrlPoster? _url;
    private readonly string _pipelineName;
    private readonly string _sinkName;

    public LoopbackInterceptor(
        ILogger innerSink,
        SinkRunStateHolder state,
        SinkOverridesHolder overrides,
        ILogLevelRegistry levels,
        string pipelineName,
        string sinkName,
        LoopbackFileWriter? file,
        LoopbackUrlPoster? url)
    {
        _innerSink = innerSink ?? throw new ArgumentNullException(nameof(innerSink));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _overrides = overrides ?? throw new ArgumentNullException(nameof(overrides));
        _levels = levels ?? throw new ArgumentNullException(nameof(levels));
        _pipelineName = pipelineName ?? throw new ArgumentNullException(nameof(pipelineName));
        _sinkName = sinkName ?? throw new ArgumentNullException(nameof(sinkName));
        _file = file;
        _url = url;
    }

    public void Log(LogEvent logEvent)
    {
        var current = _state.Current;

        // Disabled is the unconditional short-circuit. Cheapest path
        // through the interceptor — one Volatile read, one branch.
        // Disabled never publishes, never tees, never writes — the
        // operator's "this sink is off" intent has to mean off.
        if (current == SinkRunState.Disabled) return;

        // Per-sink minimum-level gate. Null gate is the open path —
        // skip the registry lookup entirely. Otherwise the registry's
        // hot-path IsBelow performs two cached dictionary lookups and
        // returns. Events below the gate drop before any leg
        // consultation, but if the URL leg is configured AND the sink
        // is in a mode that would normally route to URL (test, or
        // live with teeLiveToUrl on), we publish a bus-only rejection
        // entry so the dashboard's loopback panel can render the
        // dropped event dimmed. Files don't get rejection entries;
        // bulk storage isn't a real-time inspection surface.
        var gate = _overrides.MinLevel;
        if (gate is not null && _levels.IsBelow(logEvent.Level, gate))
        {
            PublishRejectionToBus(logEvent, current, "level");
            return;
        }

        var isTest = current == SinkRunState.Test;
        var teeFile = _overrides.TeeLiveToFile;
        var teeUrl = _overrides.TeeLiveToUrl;

        // Live mode forwards to the real sink first so a failure in
        // the loopback path can't suppress the actual send.
        if (!isTest)
        {
            _innerSink.Log(logEvent);
        }

        // Decide which legs fire. Test always uses every leg the
        // pipeline configured. Live consults the per-sink tee flags
        // for file + URL, and uses the same flags for the bus so a
        // sink that opted in to live teeing also feeds the dashboard
        // panel. Bus fires unconditionally in test mode even when
        // neither file nor URL is configured — the dashboard panel
        // is the always-on observer of test traffic.
        var fireFile = _file is not null && (isTest || teeFile);
        var fireUrl  = _url  is not null && (isTest || teeUrl);
        var fireBus  = isTest || teeFile || teeUrl;

        if (!fireFile && !fireUrl && !fireBus) return;

        // Project once, dispatch many. The DTO is the same shape every
        // leg consumes; producing it here avoids walking the event
        // multiple times when several legs are active.
        var entry = ProjectEntry(logEvent);

        if (fireFile)
        {
            // Plain-text mode falls back to the rendered message
            // (LogEvent.Message). A future slice can wire the pipeline's
            // output transformer here for fully-styled plain text.
            try { _file!.Write(entry, plainTextLine: null); }
            catch { /* loopback failure must not break the sink path */ }
        }

        if (fireUrl)
        {
            try { _url!.Post(entry); }
            catch { /* same */ }
        }

        // Bus: publish whenever the gating decision said so. No-op
        // when there are no subscribers, which is the expected
        // server-without-Dashboard-open hot path.
        if (fireBus) LoopbackEventBus.Publish(_pipelineName, _sinkName, entry);
    }

    /// <summary>
    /// Publish a rejection entry to the loopback bus when the URL leg
    /// is configured AND the sink mode would normally route to it
    /// (test, or live with teeLiveToUrl on). Files never get rejection
    /// entries — bulk storage isn't a real-time inspection surface.
    /// The URL receiver doesn't get a POST either; the bus-only path
    /// keeps externally-visible traffic clean while the dashboard's
    /// SSE subscriber sees the rejection and can render it dimmed.
    /// </summary>
    protected void PublishRejectionToBus(LogEvent logEvent, SinkRunState current, string reason)
    {
        if (_url is null) return;
        var teeUrl = _overrides.TeeLiveToUrl;
        var isTest = current == SinkRunState.Test;
        if (!isTest && !teeUrl) return;
        var entry = ProjectEntry(logEvent) with { Rejected = true, RejectionReason = reason };
        LoopbackEventBus.Publish(_pipelineName, _sinkName, entry);
    }

    /// <summary>
    /// Build the on-the-wire entry from a LogEvent. Properties
    /// flatten to a string-keyed dictionary so the JSON output reads
    /// like <c>"properties": { "userId": 42, "action": "login" }</c>.
    /// </summary>
    private static LoopbackLogEntry ProjectEntry(LogEvent ev)
    {
        Dictionary<string, object?>? props = null;
        if (ev.Properties is { Count: > 0 } src)
        {
            props = new Dictionary<string, object?>(src.Count);
            for (var i = 0; i < src.Count; i++)
            {
                var p = src[i];
                if (!string.IsNullOrEmpty(p.Name))
                    props[p.Name] = p.Value;
            }
        }

        return new LoopbackLogEntry(
            TimestampUnixMs: ev.TimeUtc.ToUnixTimeMilliseconds(),
            Level: ev.Level.Key,
            Category: ev.Category.Value,
            Message: ev.Message,
            Properties: props);
    }

    public void Dispose()
    {
        // Inner sink ownership stays with the factory chain; we only
        // dispose our private file/URL legs. Interceptor disposal is
        // best-effort because the disposable chain is shared with
        // other wrappers (retry / metric / level filter).
        _file?.Dispose();
        _url?.Dispose();
        if (_innerSink is IDisposable disposable) disposable.Dispose();
    }
}

/// <summary>
/// Kernel-aware loopback interceptor. Mirrors <see cref="LoopbackInterceptor"/>
/// but additionally implements <see cref="IKernelSink"/> so the kernel
/// fast-path stays alive when the inner sink is also kernel-eligible.
///
/// <para>The buffer-path overload short-circuits to the inner kernel
/// sink whenever the runState is <c>Live</c> and neither tee flag is
/// set — that is the hot path for production where an operator never
/// touches loopback. Anything else (Disabled, Test, or Live with a
/// tee on) materialises the buffer into a heap <see cref="LogEvent"/>
/// and uses the base ILogger.Log dispatcher so the gating + leg
/// decisions stay in one place.</para>
/// </summary>
internal sealed class LoopbackInterceptorKernel : LoopbackInterceptor, IKernelSink
{
    private readonly IKernelSink _innerKernel;

    public LoopbackInterceptorKernel(
        ILogger innerSink,
        IKernelSink innerKernel,
        SinkRunStateHolder state,
        SinkOverridesHolder overrides,
        ILogLevelRegistry levels,
        string pipelineName,
        string sinkName,
        LoopbackFileWriter? file,
        LoopbackUrlPoster? url)
        : base(innerSink, state, overrides, levels, pipelineName, sinkName, file, url)
    {
        ArgumentNullException.ThrowIfNull(innerKernel);
        _innerKernel = innerKernel;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Log(in LogEventBuffer buffer)
    {
        var current = _state.Current;
        if (current == SinkRunState.Disabled) return;

        // Per-sink level gate on the kernel path. Null gate skips the
        // registry lookup entirely so the hot path stays minimal.
        // When a gate is set, we use the registry's cached IsBelow.
        var gate = _overrides.MinLevel;
        if (gate is not null && _levels.IsBelow(buffer.Level, gate))
        {
            // Materialise only when we actually need to publish — the
            // PublishRejectionToBus helper runs the URL-leg-active
            // check first and skips the projection when nothing would
            // listen. The kernel hot path stays allocation-free
            // when no URL leg is configured.
            if (_url is not null)
            {
                PublishRejectionToBus(buffer.ToLogEvent(), current, "level");
            }
            return;
        }

        // Hot path: live + no tees → buffer goes straight through to
        // the inner kernel sink. No LogEvent allocation, no leg
        // decisions, just one Volatile read between the kernel and
        // the real sink. That matches "loopback off" cost.
        if (current == SinkRunState.Live && !_overrides.TeeLiveToFile && !_overrides.TeeLiveToUrl)
        {
            _innerKernel.Log(in buffer);
            return;
        }

        // Slow path: Test, or Live with at least one tee on. Materialise
        // and dispatch through the base ILogger.Log so the gating + leg
        // logic stays in one place.
        Log(buffer.ToLogEvent());
    }
}
