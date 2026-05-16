#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;

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

        LogKernel fanOut = sinks.Count switch
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

    // Each fan-out shape wraps the per-sink call in try/catch so a single
    // throwing sink cannot kill its peers. Cost target is ≤2 ns per call in
    // the no-throw path: try/catch around a non-throwing region is a no-op
    // for the JIT once it inlines the body.
    //
    // AuditLogFailureException is rethrown — audit-mode failures are a
    // contract-level signal the caller asked to see. Everything else is
    // routed to System.Diagnostics.Trace so it lands in a debugger / ETW
    // listener / TraceListener without dragging ILogFailureSink into the
    // kernel surface. A future revision can thread a failure-sink delegate
    // through CompileFanOut if a richer audit trail is needed (tracked in
    // audit-followup.md). BCL-only, AOT-safe, zero deps.
    private static LogKernel BindSingle(IKernelSink sink) =>
        (in LogEventBuffer buffer) =>
        {
            try { sink.Log(in buffer); }
            catch (Failures.AuditLogFailureException) { throw; }
            catch (Exception ex) { ReportKernelSinkFailure(sink, ex); }
        };

    private static LogKernel BindPair(IKernelSink a, IKernelSink b) =>
        (in LogEventBuffer buffer) =>
        {
            try { a.Log(in buffer); }
            catch (Failures.AuditLogFailureException) { throw; }
            catch (Exception ex) { ReportKernelSinkFailure(a, ex); }

            try { b.Log(in buffer); }
            catch (Failures.AuditLogFailureException) { throw; }
            catch (Exception ex) { ReportKernelSinkFailure(b, ex); }
        };

    private static LogKernel BindTriple(IKernelSink a, IKernelSink b, IKernelSink c) =>
        (in LogEventBuffer buffer) =>
        {
            try { a.Log(in buffer); }
            catch (Failures.AuditLogFailureException) { throw; }
            catch (Exception ex) { ReportKernelSinkFailure(a, ex); }

            try { b.Log(in buffer); }
            catch (Failures.AuditLogFailureException) { throw; }
            catch (Exception ex) { ReportKernelSinkFailure(b, ex); }

            try { c.Log(in buffer); }
            catch (Failures.AuditLogFailureException) { throw; }
            catch (Exception ex) { ReportKernelSinkFailure(c, ex); }
        };

    // Loop fan-out for 4+ sinks. The array is captured once, so the per-call
    // cost is one bounds-checked indexer per sink plus one virtual call
    // through IKernelSink. The JIT usually lifts the bounds check outside a
    // known-length loop. Per-sink try/catch preserves peer delivery on the
    // same isolation contract as the unrolled shapes above.
    private static LogKernel BindMany(IKernelSink[] sinks) =>
        (in LogEventBuffer buffer) =>
        {
            for (var i = 0; i < sinks.Length; i++)
            {
                var sink = sinks[i];
                try { sink.Log(in buffer); }
                catch (Failures.AuditLogFailureException) { throw; }
                catch (Exception ex) { ReportKernelSinkFailure(sink, ex); }
            }
        };

    // Lifted out of the hot path so the catch site stays a tiny call. Writing
    // through Trace.WriteLine keeps the kernel free of any failure-sink
    // dependency — listeners that want richer routing can attach a
    // TraceListener, and the OSS surface stays BCL-only.
    private static void ReportKernelSinkFailure(IKernelSink sink, Exception ex)
    {
        try
        {
            Trace.WriteLine(
                $"[Herald.OSS] kernel sink threw: {sink.GetType().Name}: {ex.GetType().Name}: {ex.Message}");
        }
        catch
        {
            // Trace listeners can throw; the kernel must never propagate a
            // listener failure back to the caller. Swallow defensively — the
            // original exception has already been isolated from peer sinks,
            // which is the contract this method exists to preserve.
        }
    }

    private static IKernelSink[] SnapshotKernelSinks(IReadOnlyList<ILogger> sinks)
    {
        var result = new IKernelSink[sinks.Count];
        for (var i = 0; i < sinks.Count; i++)
        {
            result[i] = (IKernelSink)sinks[i];
        }
        return result;
    }
}
