#nullable enable

using System;
using MMP.Herald.Events;
using MMP.Herald.Templating;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// A predicate over a kernel-path event. Returns <c>true</c> when the event
/// should be <b>excluded</b> (dropped before the inner sink), <c>false</c>
/// when it should pass through. Modelled on Serilog's
/// <c>Filter.ByExcluding(Func&lt;LogEvent, bool&gt;)</c>, but evaluated
/// against the stack <see cref="LogEventBuffer"/> so the common case allocates
/// nothing.
///
/// <para>
/// The delegate must read producer-thread-eager fields only — the buffer's
/// own properties, level, category, template. It must not capture mutable
/// state or read ambient context (<c>AsyncLocal</c>, <c>HttpContext</c>); the
/// same lazy-resolution discipline that governs property capture applies here.
/// A <c>static</c> lambda keeps the steady state at 0 B/op.
/// </para>
/// </summary>
public delegate bool LogEventBufferPredicate(in LogEventBuffer buffer);

/// <summary>
/// Per-event property filter for the kernel fast path. Wraps an inner
/// <see cref="IKernelSink"/> and drops events the configured predicate
/// excludes; everything else forwards unchanged.
///
/// <para>
/// Cut from the <see cref="LevelFilteredKernelSink"/> template — same
/// dual-entry shape (a <c>Log(in LogEventBuffer)</c> hot path plus a
/// <c>Log(LogEvent)</c> heap twin), same forward-to-inner discipline. The
/// difference is the admission test: a level floor there, an arbitrary
/// property predicate here.
/// </para>
///
/// <para>
/// <b>One predicate, two entry points, proven equal.</b> Both the buffer path
/// and the heap path evaluate the <i>same</i> <see cref="LogEventBufferPredicate"/>.
/// The heap twin does not re-implement the decision in terms of
/// <see cref="LogEvent"/> — it wraps the event in a buffer view via
/// <see cref="KernelBufferAdapter.AsBuffer"/> and runs the identical delegate.
/// That structural sharing is what closes the silent-divergence bug class
/// (a buffer that drops an event the heap path would have kept, or the
/// reverse). The equality is enforced by construction, not by parallel code,
/// and pinned by a regression test.
/// </para>
///
/// <para>
/// <b>Allocation.</b> Steady state is 0 B/op on the buffer path: the predicate
/// runs over stack spans and a static lambda captures nothing. The heap path
/// pays one defensive property-span copy only when the event's property list
/// is neither a <c>LogProperty[]</c> nor a <c>List&lt;LogProperty&gt;</c> —
/// an uncommon backing the hot path never produces.
/// </para>
/// </summary>
public sealed class PredicateFilteredKernelSink : ILogger, IKernelSink
{
    private readonly IKernelSink _innerKernel;
    private readonly ILogger _innerLogger;
    private readonly LogEventBufferPredicate _exclude;

    // Heap-twin scratch for the uncommon non-array property backing. Single
    // instance per sink, reused across heap-path calls; never touched by the
    // buffer hot path. Not thread-safe to share, so the heap path is the only
    // writer — see Log(LogEvent) below.
    [ThreadStatic]
    private static LogProperty[]? _heapScratch;

    /// <summary>
    /// Wrap <paramref name="inner"/> so events matching <paramref name="exclude"/>
    /// are dropped. The inner sink must implement <see cref="IKernelSink"/> —
    /// this decorator only sits on the kernel path.
    /// </summary>
    public PredicateFilteredKernelSink(ILogger inner, LogEventBufferPredicate exclude)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(exclude);

        _innerLogger = inner;
        _innerKernel = inner as IKernelSink
            ?? throw new ArgumentException(
                $"PredicateFilteredKernelSink requires an inner sink that implements IKernelSink; got {inner.GetType().Name}.",
                nameof(inner));
        _exclude = exclude;
    }

    /// <summary>Hot path: evaluate the predicate over the stack buffer.</summary>
    public void Log(in LogEventBuffer buffer)
    {
        if (_exclude(in buffer)) return;
        _innerKernel.Log(in buffer);
    }

    /// <summary>
    /// Heap twin: wrap the event in a buffer view and run the <i>same</i>
    /// predicate, so the decision matches the hot path exactly.
    /// </summary>
    public void Log(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        var view = KernelBufferAdapter.AsBuffer(logEvent, ref _heapScratch);
        if (_exclude(in view)) return;
        _innerLogger.Log(logEvent);
    }

    // -- Inspection --

    /// <summary>The downstream sink that admitted events forward to.</summary>
    public ILogger Inner => _innerLogger;
}
