#nullable enable

using System;
using System.Collections.Generic;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Builds a <see cref="LogKernel"/> for the configured sinks. The compiler is
/// intentionally small — it stitches pre-written fan-out shapes together
/// rather than emitting IL, which keeps the implementation readable and
/// removes an entire class of runtime-IL-generation risk (AOT compatibility,
/// startup cost, debugger surface).
///
/// The chosen shape depends on sink count. Hand-unrolled fan-outs for 1, 2,
/// and 3 sinks cover the overwhelming majority of real configurations at the
/// lowest possible dispatch cost. Four or more sinks fall through to a loop
/// over a captured array — still fast, still inline-friendly in the JIT.
/// </summary>
public static class KernelCompiler
{
    /// <summary>
    /// Produce a kernel that fans out a buffer to every sink. Callers must
    /// have already verified via <see cref="KernelEligibility.IsEligible"/>
    /// that every sink implements <see cref="IKernelSink"/> — this is not
    /// re-checked here to keep the hot path free of defensive casts.
    /// </summary>
    public static LogKernel CompileFanOut(IReadOnlyList<ILogger> sinks) =>
        CompileFanOut(sinks, enrichers: null);

    /// <summary>
    /// Same as <see cref="CompileFanOut(IReadOnlyList{ILogger})"/> but with
    /// an optional list of <see cref="IKernelEnricher"/>s run before fan-out.
    /// Each enricher observes the buffer in order; none are permitted to
    /// retain the buffer past their call.
    /// </summary>
    public static LogKernel CompileFanOut(
        IReadOnlyList<ILogger> sinks,
        IReadOnlyList<IKernelEnricher>? enrichers) =>
        CompileFanOut(sinks, enrichers, decorators: null);

    /// <summary>
    /// Full-form kernel compilation: sinks, optional enrichers, and optional
    /// kernel decorators. Decorators wrap the (enrichment + fan-out) kernel
    /// in registration order; the first decorator sees a buffer first.
    /// </summary>
    public static LogKernel CompileFanOut(
        IReadOnlyList<ILogger> sinks,
        IReadOnlyList<IKernelEnricher>? enrichers,
        IReadOnlyList<IKernelDecorator>? decorators)
    {
        ArgumentNullException.ThrowIfNull(sinks);

        // When every fan-out target is a GenSourceGatedKernelSink we can
        // unwrap them at compose time, capture the reference token once, and
        // emit a closure that ref-equals-checks the buffer's GenSource and
        // calls the inner kernel sink directly — no per-event dispatch
        // through the wrapper. Mixed fan-outs (gated + raw) fall back to the
        // standard BindXxx variants; the gates handle their own validation
        // through their virtual Log overload in that case.
        var fanOut = AllGated(sinks)
            ? sinks.Count switch
            {
                0 => NoSinks,
                1 => BindSingleGated(
                    (GenSourceGatedKernelSink)sinks[0]),
                2 => BindPairGated(
                    (GenSourceGatedKernelSink)sinks[0],
                    (GenSourceGatedKernelSink)sinks[1]),
                3 => BindTripleGated(
                    (GenSourceGatedKernelSink)sinks[0],
                    (GenSourceGatedKernelSink)sinks[1],
                    (GenSourceGatedKernelSink)sinks[2]),
                _ => BindManyGatedFromList(sinks),
            }
            : sinks.Count switch
            {
                0 => NoSinks,
                1 => BindSingle((IKernelSink)sinks[0]),
                2 => BindPair((IKernelSink)sinks[0], (IKernelSink)sinks[1]),
                3 => BindTriple((IKernelSink)sinks[0], (IKernelSink)sinks[1], (IKernelSink)sinks[2]),
                _ => BindMany(SnapshotKernelSinks(sinks)),
            };

        LogKernel withEnrichers = fanOut;
        if (enrichers is { Count: > 0 })
        {
            var capturedEnrichers = SnapshotEnrichers(enrichers);
            LogKernel inner = withEnrichers;
            withEnrichers = (in LogEventBuffer buffer) =>
            {
                for (var i = 0; i < capturedEnrichers.Length; i++)
                {
                    capturedEnrichers[i].Enrich(in buffer);
                }
                inner(in buffer);
            };
        }

        if (decorators is null or { Count: 0 }) return withEnrichers;

        // Wrap decorators in reverse so the first registered runs outermost.
        var result = withEnrichers;
        for (var i = decorators.Count - 1; i >= 0; i--)
        {
            result = decorators[i].Wrap(result);
        }
        return result;
    }

    private static IKernelEnricher[] SnapshotEnrichers(IReadOnlyList<IKernelEnricher> enrichers)
    {
        var result = new IKernelEnricher[enrichers.Count];
        for (var i = 0; i < enrichers.Count; i++)
        {
            result[i] = enrichers[i];
        }
        return result;
    }

    // Null-object kernel: accepted by StructuredLogger but does nothing. Used
    // when the caller wires up zero sinks — safer than throwing, lets the
    // caller add sinks later without reconstructing the pipeline.
    private static void NoSinks(in LogEventBuffer buffer) { }

    // ── Sink-bind dispatchers ─────────────────────────────────────────────
    //
    // Two variants per arity. The plain ones — BindSingle / BindPair /
    // BindTriple / BindMany — call IKernelSink.Log directly and stay on
    // the un-gated fast path. The gated variants —
    // BindSingleGated / BindPairGated / BindTripleGated / BindManyGated —
    // are picked when every sink in the fan-out is a
    // GenSourceGatedKernelSink. They capture the inner sinks and the
    // pipeline's reference token at compose time and inline a
    // ReferenceEquals check into the closure: the common case (events
    // stamped with the pipeline's reference token) skips the gate's
    // virtual dispatch entirely and calls the inner kernel sink directly.
    // External-source events fall through to the gate's full IsAccepted
    // check.

    private static LogKernel BindSingle(IKernelSink sink) =>
        (in LogEventBuffer buffer) => sink.Log(in buffer);

    private static LogKernel BindPair(IKernelSink a, IKernelSink b) =>
        (in LogEventBuffer buffer) =>
        {
            a.Log(in buffer);
            b.Log(in buffer);
        };

    private static LogKernel BindTriple(IKernelSink a, IKernelSink b, IKernelSink c) =>
        (in LogEventBuffer buffer) =>
        {
            a.Log(in buffer);
            b.Log(in buffer);
            c.Log(in buffer);
        };

    // Loop fan-out for 4+ sinks. The array is captured once, so the per-call
    // cost is one bounds-checked indexer per sink plus one virtual call
    // through IKernelSink. The JIT usually lifts the bounds check outside a
    // known-length loop.
    private static LogKernel BindMany(IKernelSink[] sinks) =>
        (in LogEventBuffer buffer) =>
        {
            for (var i = 0; i < sinks.Length; i++)
            {
                sinks[i].Log(in buffer);
            }
        };

    private static LogKernel BindSingleGated(GenSourceGatedKernelSink gate)
    {
        var inner = gate.InnerKernel;
        var refToken = gate.ReferenceSource;
        return (in LogEventBuffer buffer) =>
        {
            if (ReferenceEquals(buffer.GenSource, refToken))
            {
                inner.Log(in buffer);
                return;
            }
            if (gate.IsAccepted(buffer.GenSource))
            {
                inner.Log(in buffer);
            }
        };
    }

    private static LogKernel BindPairGated(
        GenSourceGatedKernelSink a, GenSourceGatedKernelSink b)
    {
        var ai = a.InnerKernel;
        var bi = b.InnerKernel;
        // Every gate in a pipeline shares the same reference token by
        // construction (the bootstrap seeds them all from the same
        // string instance), so one ref-equals on the first gate gates
        // every fan-out target on the fast path.
        var refToken = a.ReferenceSource;
        return (in LogEventBuffer buffer) =>
        {
            if (ReferenceEquals(buffer.GenSource, refToken))
            {
                ai.Log(in buffer);
                bi.Log(in buffer);
                return;
            }
            if (a.IsAccepted(buffer.GenSource)) ai.Log(in buffer);
            if (b.IsAccepted(buffer.GenSource)) bi.Log(in buffer);
        };
    }

    private static LogKernel BindTripleGated(
        GenSourceGatedKernelSink a, GenSourceGatedKernelSink b, GenSourceGatedKernelSink c)
    {
        var ai = a.InnerKernel;
        var bi = b.InnerKernel;
        var ci = c.InnerKernel;
        var refToken = a.ReferenceSource;
        return (in LogEventBuffer buffer) =>
        {
            if (ReferenceEquals(buffer.GenSource, refToken))
            {
                ai.Log(in buffer);
                bi.Log(in buffer);
                ci.Log(in buffer);
                return;
            }
            if (a.IsAccepted(buffer.GenSource)) ai.Log(in buffer);
            if (b.IsAccepted(buffer.GenSource)) bi.Log(in buffer);
            if (c.IsAccepted(buffer.GenSource)) ci.Log(in buffer);
        };
    }

    private static LogKernel BindManyGated(
        GenSourceGatedKernelSink[] gates, IKernelSink[] inners, string refToken) =>
        (in LogEventBuffer buffer) =>
        {
            if (ReferenceEquals(buffer.GenSource, refToken))
            {
                for (var i = 0; i < inners.Length; i++)
                {
                    inners[i].Log(in buffer);
                }
                return;
            }
            for (var i = 0; i < gates.Length; i++)
            {
                if (gates[i].IsAccepted(buffer.GenSource))
                {
                    inners[i].Log(in buffer);
                }
            }
        };

    private static IKernelSink[] SnapshotKernelSinks(IReadOnlyList<ILogger> sinks)
    {
        var result = new IKernelSink[sinks.Count];
        for (var i = 0; i < sinks.Count; i++)
        {
            result[i] = (IKernelSink)sinks[i];
        }
        return result;
    }

    private static bool AllGated(IReadOnlyList<ILogger> sinks)
    {
        if (sinks.Count == 0) return false;
        for (var i = 0; i < sinks.Count; i++)
        {
            if (sinks[i] is not GenSourceGatedKernelSink) return false;
        }
        return true;
    }

    private static LogKernel BindManyGatedFromList(IReadOnlyList<ILogger> sinks)
    {
        var gates = new GenSourceGatedKernelSink[sinks.Count];
        var inners = new IKernelSink[sinks.Count];
        for (var i = 0; i < sinks.Count; i++)
        {
            var gate = (GenSourceGatedKernelSink)sinks[i];
            gates[i] = gate;
            inners[i] = gate.InnerKernel;
        }
        return BindManyGated(gates, inners, gates[0].ReferenceSource);
    }
}
