#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using MMP.Herald.Diagnostics;
using MMP.Herald.Events;
using MMP.Herald.Templating;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Per-sink provenance gate. Wraps an inner sink and only forwards events
/// whose <see cref="LogEvent.GenSource"/> (or <see cref="LogEventBuffer.GenSource"/>)
/// matches one of the accepted sources this gate carries.
///
/// <para>
/// Architectural seam. Herald.OSS does not stamp GenSource on events through
/// its default factories, and no OSS code path constructs a gate by default;
/// the gate class is present so downstream commercial wrappers (or an OSS
/// adopter that wants multi-tenant routing without per-sink code) can wrap
/// sinks at composition time and inject reference / external source tokens.
/// </para>
///
/// <para>
/// Every gate is constructed with the pipeline's <i>reference</i> source —
/// the token the pipeline's event factory stamps on internal events.
/// External callers that need to inject events directly into a sink push
/// raw keys into the gates of the named sinks via
/// <see cref="RegisterAcceptedSource(string)"/>. After registration, events
/// stamped with that raw key pass the gate; events with any other (or null)
/// <c>GenSource</c> are silently dropped.
/// </para>
///
/// <para>
/// <b>Hot-path shape.</b> The reference-source check uses
/// <see cref="object.ReferenceEquals(object?, object?)"/> as the first
/// gate — internal events from the pipeline share the exact string instance
/// the gate carries, so the common case is one pointer compare. The
/// fallbacks (string equality, then HashSet lookup) only run when the
/// reference path misses, which by construction means the event came from
/// outside the owning pipeline.
/// </para>
///
/// <para>
/// <b>Concurrency.</b> Registrations take a lock on the gate so additions
/// are serialized; per-event reads do a <see cref="Volatile.Read{T}"/> on
/// the additional-sources field so they observe a consistent snapshot
/// without locking. The HashSet itself is treated as immutable once
/// published — <see cref="RegisterAcceptedSource(string)"/> copies on write.
/// </para>
///
/// <para>
/// <b>Wrapping shape.</b> The gate implements both <see cref="ILogger"/>
/// and <see cref="IKernelSink"/>; it forwards to the inner sink's matching
/// overload. Sinks that don't implement <see cref="IKernelSink"/> simply
/// don't expose the kernel-path overload — the kernel composition layer
/// already handles that case.
/// </para>
/// </summary>
public class GenSourceGatedSink : ILogger, IDisposable
{
    private readonly ILogger _inner;
    private readonly string _referenceSource;
    private readonly string _label;
    private readonly Action<string, string>? _onRejection;
    private readonly object _lock = new();

    // Volatile so per-event readers observe the latest registered set
    // without locking. Replaced (not mutated) by RegisterAcceptedSource
    // under _lock — readers always see a fully-initialised snapshot.
    private HashSet<string>? _additionalSources;

    /// <summary>
    /// Wrap <paramref name="inner"/> with a gate. When inner implements
    /// <see cref="IKernelSink"/>, returns a <see cref="GenSourceGatedKernelSink"/>
    /// so the kernel-eligibility check still picks up the wrapped sink as
    /// kernel-capable; otherwise returns a plain <see cref="GenSourceGatedSink"/>.
    /// Centralizing the choice here keeps callers from getting it wrong
    /// — wrong choice = a non-kernel sink ends up on the kernel path and
    /// receives an unrendered <see cref="LogEventBuffer"/>.
    /// </summary>
    /// <param name="label">
    /// Per-sink label used by the security registrar to identify this sink
    /// for external-source registrations. Pass null or an empty string to
    /// have the wrapper auto-generate a 32-hex-char random label.
    /// </param>
    public static GenSourceGatedSink Wrap(
        ILogger inner,
        string referenceSource,
        Action<string, string>? onRejection = null,
        string? label = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return inner is IKernelSink kernel
            ? new GenSourceGatedKernelSink(inner, kernel, referenceSource, onRejection, label)
            : new GenSourceGatedSink(inner, referenceSource, onRejection, label);
    }

    /// <param name="inner">The wrapped sink.</param>
    /// <param name="referenceSource">
    /// The pipeline's reference token. Stamped on every internal event by
    /// the pipeline's event factory; the sink-side gate uses it as the
    /// always-accepted source.
    /// </param>
    /// <param name="onRejection">
    /// Optional callback invoked when an event is rejected. Receives the
    /// rejected event's source token (or "(null)") and the wrapped sink's
    /// type name. Intended for diagnostics — leave null in production.
    /// </param>
    /// <param name="label">
    /// Per-sink label used by the security registrar. Null or empty
    /// triggers auto-generation; any other value is preserved verbatim.
    /// </param>
    public GenSourceGatedSink(
        ILogger inner,
        string referenceSource,
        Action<string, string>? onRejection = null,
        string? label = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrEmpty(referenceSource);

        _inner = inner;
        _referenceSource = referenceSource;
        _onRejection = onRejection;
        _label = string.IsNullOrEmpty(label) ? Guid.NewGuid().ToString("N") : label;
    }

    /// <summary>
    /// Per-sink label. Either the value the operator pre-defined on the
    /// builder/config, or an auto-generated 32-hex-char random string when
    /// the operator passed null or empty. A downstream registrar uses this
    /// to identify which sinks an external-injection registration targets,
    /// independent of the config <c>Name</c>.
    /// </summary>
    public string Label => _label;

    /// <summary>
    /// The wrapped sink. Exposed so the pipeline composition layer can still
    /// see what's behind the gate (kernel eligibility, sink-chain
    /// introspection, debug surfaces).
    /// </summary>
    public ILogger Inner => _inner;

    /// <summary>
    /// The pipeline's reference token — same string instance the event
    /// factory stamps on every internal event. A kernel compiler can read
    /// this so it can inline the ref-equals fast path directly into the
    /// kernel closure, skipping the wrapper-as-virtual-stop for events
    /// from the owning pipeline.
    /// </summary>
    public string ReferenceSource => _referenceSource;

    /// <summary>
    /// Add <paramref name="source"/> to the accepted-source list. Called by
    /// a downstream registrar after the registrar's timestamp lock allowed
    /// the registration. Safe to call from any thread.
    /// </summary>
    public void RegisterAcceptedSource(string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        lock (_lock)
        {
            // Copy-on-write so readers see fully-initialised sets.
            var existing = _additionalSources;
            var next = existing is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(existing, StringComparer.Ordinal);
            next.Add(source);
            Volatile.Write(ref _additionalSources, next);
        }
    }

    /// <summary>
    /// Remove <paramref name="source"/> from the accepted-source list.
    /// Used during hot reload when an external registration is being
    /// withdrawn or replaced.
    /// </summary>
    public void RevokeAcceptedSource(string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        lock (_lock)
        {
            var existing = _additionalSources;
            if (existing is null || !existing.Contains(source)) return;

            var next = new HashSet<string>(existing, StringComparer.Ordinal);
            next.Remove(source);
            Volatile.Write(ref _additionalSources, next.Count == 0 ? null : next);
        }
    }

    /// <summary>
    /// True when an event stamped with <paramref name="eventGenSource"/>
    /// is allowed to reach the wrapped sink. Reference-equality fast path
    /// for internal events; string equality + HashSet for external sources.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAccepted(string? eventGenSource)
    {
        if (ReferenceEquals(eventGenSource, _referenceSource)) return true;
        if (eventGenSource is null) return false;
        if (eventGenSource == _referenceSource) return true;

        var additional = Volatile.Read(ref _additionalSources);
        return additional is not null && additional.Contains(eventGenSource);
    }

    public virtual void Log(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        if (!IsAccepted(logEvent.GenSource))
        {
            EmitRejectionNotice(logEvent.GenSource, _inner.GetType().Name);
            return;
        }
        _inner.Log(logEvent);
    }

    public virtual System.Threading.Tasks.ValueTask LogAsync(
        LogEvent logEvent,
        System.Threading.CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        if (!IsAccepted(logEvent.GenSource))
        {
            EmitRejectionNotice(logEvent.GenSource, _inner.GetType().Name);
            return System.Threading.Tasks.ValueTask.CompletedTask;
        }
        return _inner.LogAsync(logEvent, cancellationToken);
    }

    public void Dispose() => (_inner as IDisposable)?.Dispose();

    // The runtime-notice source token for the default (no-callback) rejection
    // notice. Same @herald.runtime.<topic> channel the consent-off injection
    // refusal and the naming-policy announcement use, so an operator sees gate
    // drops and consent-off drops the same way (ADR section 7.6).
    private const string GateRejectionNoticeSource = "@herald.runtime.gen-source-gate";

    /// <summary>
    /// Surface a gate rejection. When an explicit <c>onRejection</c> callback
    /// was supplied at construction it wins (diagnostic hook, test capture).
    /// Otherwise — the production default — a notice is published on
    /// <see cref="HeraldRuntimeMessages"/> instead of the event vanishing
    /// silently. This is the section 7.6 pairing with the consent-off injection
    /// notice: a dropped event now leaves a located trace on the same channel
    /// whether it was dropped for missing consent or for failing the gate.
    /// </summary>
    protected void EmitRejectionNotice(string? eventGenSource, string sinkTypeName)
    {
        var source = eventGenSource ?? "(null)";
        if (_onRejection is { } callback)
        {
            callback(source, sinkTypeName);
            return;
        }

        HeraldRuntimeMessages.Publish(
            GateRejectionNoticeSource,
            $"Herald's provenance gate dropped an event bound for sink '{sinkTypeName}': its " +
            $"GenSource '{source}' is not on the gate's accept list. The event was built or " +
            "injected without a GenSource the gate recognises. Register the source via the " +
            "external-source registrar, or push the event through the typed surface so the " +
            "pipeline stamps it.",
            NoticeSeverity.Warning,
            new List<LogProperty>(2)
            {
                new("genSource", source),
                new("sink", sinkTypeName),
            });
    }

    // Protected accessors so the kernel-aware subtype can read shared state
    // without duplicating the validation logic.
    protected ILogger Inner_ => _inner;
    protected Action<string, string>? OnRejection_ => _onRejection;
}

/// <summary>
/// Kernel-aware gate. Used when the wrapped sink implements
/// <see cref="IKernelSink"/> — the gate itself implements <c>IKernelSink</c>
/// so kernel-eligibility checks see the wrapped chain as eligible, and the
/// buffer-path overload validates and forwards directly to the inner kernel
/// sink without materialising a <see cref="LogEvent"/>.
/// </summary>
public sealed class GenSourceGatedKernelSink : GenSourceGatedSink, IKernelSink
{
    private readonly IKernelSink _innerKernel;

    public GenSourceGatedKernelSink(
        ILogger inner,
        IKernelSink innerKernel,
        string referenceSource,
        Action<string, string>? onRejection = null,
        string? label = null)
        : base(inner, referenceSource, onRejection, label)
    {
        ArgumentNullException.ThrowIfNull(innerKernel);
        _innerKernel = innerKernel;
    }

    /// <summary>
    /// The wrapped <see cref="IKernelSink"/>. Exposed so a kernel compiler
    /// can call it directly when it inlines the gate's check into the kernel
    /// closure — restores the un-gated dispatch shape on the fast path.
    /// </summary>
    public IKernelSink InnerKernel => _innerKernel;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Log(in LogEventBuffer buffer)
    {
        if (!IsAccepted(buffer.GenSource))
        {
            EmitRejectionNotice(buffer.GenSource, Inner_.GetType().Name);
            return;
        }
        _innerKernel.Log(in buffer);
    }
}
