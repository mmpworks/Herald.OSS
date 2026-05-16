#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using MMP.Herald.Events;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Templating;

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
        CompileFanOut(sinks, enrichers, decorators: null, failureSink: null);

    /// <summary>
    /// Full-form kernel compilation: sinks, optional enrichers, and optional
    /// kernel decorators. Decorators wrap the (enrichment + fan-out) kernel
    /// in registration order; the first decorator sees a buffer first.
    /// </summary>
    public static LogKernel CompileFanOut(
        IReadOnlyList<ILogger> sinks,
        IReadOnlyList<IKernelEnricher>? enrichers,
        IReadOnlyList<IKernelDecorator>? decorators) =>
        CompileFanOut(sinks, enrichers, decorators, failureSink: null);

    /// <summary>
    /// Full-form kernel compilation with an optional failure sink. Per-sink
    /// throws (other than <see cref="AuditLogFailureException"/>, which still
    /// propagates) are routed through <paramref name="failureSink"/> when
    /// supplied, matching <see cref="MMP.Herald.Pipeline.SafeCompositeLogger"/>
    /// semantics on the chain path. When <paramref name="failureSink"/> is
    /// <c>null</c> the kernel falls back to
    /// <see cref="System.Diagnostics.Trace.WriteLine(string?)"/> so a vanilla
    /// pipeline still surfaces the failure through a debugger / ETW / trace
    /// listener — no silent drop.
    /// </summary>
    public static LogKernel CompileFanOut(
        IReadOnlyList<ILogger> sinks,
        IReadOnlyList<IKernelEnricher>? enrichers,
        IReadOnlyList<IKernelDecorator>? decorators,
        ILogFailureSink? failureSink)
    {
        ArgumentNullException.ThrowIfNull(sinks);

        LogKernel fanOut = sinks.Count switch
        {
            0 => NoSinks,
            1 => BindSingle((IKernelSink)sinks[0], failureSink),
            2 => BindPair((IKernelSink)sinks[0], (IKernelSink)sinks[1], failureSink),
            3 => BindTriple((IKernelSink)sinks[0], (IKernelSink)sinks[1], (IKernelSink)sinks[2], failureSink),
            _ => BindMany(SnapshotKernelSinks(sinks), failureSink),
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
    // routed through ReportSinkFailure: a configured ILogFailureSink receives
    // a synthesized LogEvent (matching SafeCompositeLogger's chain-path
    // shape); when no failure sink is wired the report falls back to
    // System.Diagnostics.Trace so a debugger / ETW / TraceListener still
    // surfaces the throw.
    private static LogKernel BindSingle(IKernelSink sink, ILogFailureSink? failureSink) =>
        (in LogEventBuffer buffer) =>
        {
            try { sink.Log(in buffer); }
            catch (AuditLogFailureException) { throw; }
            catch (Exception ex) { ReportSinkFailure(failureSink, sink, ex, in buffer); }
        };

    private static LogKernel BindPair(IKernelSink a, IKernelSink b, ILogFailureSink? failureSink) =>
        (in LogEventBuffer buffer) =>
        {
            try { a.Log(in buffer); }
            catch (AuditLogFailureException) { throw; }
            catch (Exception ex) { ReportSinkFailure(failureSink, a, ex, in buffer); }

            try { b.Log(in buffer); }
            catch (AuditLogFailureException) { throw; }
            catch (Exception ex) { ReportSinkFailure(failureSink, b, ex, in buffer); }
        };

    private static LogKernel BindTriple(IKernelSink a, IKernelSink b, IKernelSink c, ILogFailureSink? failureSink) =>
        (in LogEventBuffer buffer) =>
        {
            try { a.Log(in buffer); }
            catch (AuditLogFailureException) { throw; }
            catch (Exception ex) { ReportSinkFailure(failureSink, a, ex, in buffer); }

            try { b.Log(in buffer); }
            catch (AuditLogFailureException) { throw; }
            catch (Exception ex) { ReportSinkFailure(failureSink, b, ex, in buffer); }

            try { c.Log(in buffer); }
            catch (AuditLogFailureException) { throw; }
            catch (Exception ex) { ReportSinkFailure(failureSink, c, ex, in buffer); }
        };

    // Loop fan-out for 4+ sinks. The array is captured once, so the per-call
    // cost is one bounds-checked indexer per sink plus one virtual call
    // through IKernelSink. The JIT usually lifts the bounds check outside a
    // known-length loop. Per-sink try/catch preserves peer delivery on the
    // same isolation contract as the unrolled shapes above.
    private static LogKernel BindMany(IKernelSink[] sinks, ILogFailureSink? failureSink) =>
        (in LogEventBuffer buffer) =>
        {
            for (var i = 0; i < sinks.Length; i++)
            {
                var sink = sinks[i];
                try { sink.Log(in buffer); }
                catch (AuditLogFailureException) { throw; }
                catch (Exception ex) { ReportSinkFailure(failureSink, sink, ex, in buffer); }
            }
        };

    // Dispatch the failure to whatever channel is configured. The buffer is
    // a ref struct so it cannot leave this call frame; we read the fields we
    // need into a synthesized LogEvent right here (only when a failure sink
    // is actually wired — the Trace fallback path stays allocation-free).
    //
    // The synthesized event mirrors what SafeCompositeLogger.HandleChildException
    // hands to the failure sink: the original event's level/category/template,
    // empty properties (the buffer's spans cannot be widened to IReadOnlyList
    // without copying, and the failure sink doesn't need the property payload
    // to route the exception), and the buffer's timestamp.
    private static void ReportSinkFailure(
        ILogFailureSink? failureSink, IKernelSink sink, Exception ex, in LogEventBuffer buffer)
    {
        if (failureSink is null)
        {
            ReportSinkFailureToTrace(sink, ex);
            return;
        }

        var failureEvent = new LogEvent(
            TimeUtc: buffer.TimeUtc,
            Level: buffer.Level,
            Category: buffer.Category,
            MessageTemplate: buffer.MessageTemplate,
            Message: buffer.Message,
            Properties: LogEvent.EmptyProperties,
            Context: LogEvent.EmptyContext,
            EventId: buffer.EventId);

        try
        {
            failureSink.ReportFailure(failureEvent, ex, sink.GetType().Name);
        }
        catch
        {
            // A misbehaving failure sink must never propagate back to the
            // caller — peer-isolation is the whole point of this code path.
            // Fall through to Trace so the failure isn't silently lost.
            ReportSinkFailureToTrace(sink, ex);
        }
    }

    // Lifted out of the hot path so the catch site stays a tiny call. Writing
    // through Trace.WriteLine keeps the no-failure-sink fallback free of any
    // dependency — listeners that want richer routing can attach a
    // TraceListener, and the OSS surface stays BCL-only.
    private static void ReportSinkFailureToTrace(IKernelSink sink, Exception ex)
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
