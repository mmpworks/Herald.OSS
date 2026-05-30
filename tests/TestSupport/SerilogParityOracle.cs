#nullable enable

using System;
using System.Collections.Concurrent;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MMP.Herald.OSS.Tests.TestSupport;

/// <summary>
/// Parity oracle for G-VM.* Serilog-compat tests.
///
/// The oracle runs the SAME logging call through real Serilog 4.3.1 and through
/// the MMP.Herald.Serilog shim, captures both events, and provides them to the
/// test for canonical-shape comparison.
///
/// Per the ingress↔output canonical-equivalence rule, comparison is NOT
/// byte-identical: it asserts that the same property names and values appear
/// in both events after normalisation, not that the underlying representation
/// is the same.
///
/// <b>Current state (Task 1):</b> the Serilog-capture half is fully wired.
/// The shim-capture half is a TODO stub — it will be filled in during Task 8
/// when MMP.Herald.Serilog ships its value-model mirror types. Tests that
/// reference CaptureShim() will not compile until Task 8 lands.
/// </summary>
public sealed class SerilogParityOracle
{
    // ---------------------------------------------------------------
    // Real-Serilog half — fully functional.
    // ---------------------------------------------------------------

    /// <summary>
    /// Runs <paramref name="log"/> against a real Serilog logger backed by an
    /// in-memory capture sink, and returns the single captured LogEvent.
    ///
    /// Throws <see cref="InvalidOperationException"/> if the action produces
    /// zero or more than one event — tests must call it with an action that
    /// emits exactly one.
    /// </summary>
    public static Serilog.Events.LogEvent CaptureSerilog(Action<Serilog.ILogger> log)
    {
        if (log is null) throw new ArgumentNullException(nameof(log));

        var sink = new CapturingSink();

        using var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        log(logger);

        var events = sink.Captured.ToArray();

        return events.Length switch
        {
            0 => throw new InvalidOperationException(
                "SerilogParityOracle.CaptureSerilog: the log action produced no events. " +
                "Ensure the action logs at a level that passes MinimumLevel.Verbose()."),
            1 => events[0],
            _ => throw new InvalidOperationException(
                $"SerilogParityOracle.CaptureSerilog: expected 1 event, got {events.Length}. " +
                "Wrap a single log call per CaptureSerilog() invocation.")
        };
    }

    // ---------------------------------------------------------------
    // Shim half — TODO: fill in during Task 8.
    // ---------------------------------------------------------------

    // TODO (Task 8): uncomment and implement once MMP.Herald.Serilog.Events.LogEvent exists.
    //
    // public static MMP.Herald.Serilog.Events.LogEvent CaptureShim(Action<MMP.Herald.Serilog.ILogger> log)
    // {
    //     // Wire the shim logger against TestLoggers.CreateCapturing(),
    //     // run the action, and return the single captured shim LogEvent
    //     // for canonical-shape comparison with CaptureSerilog().
    //     throw new NotImplementedException("Task 8: shim-capture side not yet implemented.");
    // }

    // ---------------------------------------------------------------
    // Private capture sink (Serilog ILogEventSink)
    // ---------------------------------------------------------------

    /// <summary>
    /// Thread-safe in-memory Serilog sink that accumulates emitted events.
    /// Kept private to the oracle — callers only see the final captured event,
    /// not the sink itself.
    /// </summary>
    private sealed class CapturingSink : ILogEventSink
    {
        // ConcurrentBag is sufficient: the oracle always reads after the logger
        // is disposed, so ordering does not matter (and Serilog is single-threaded
        // per-sink anyway in this synchronous configuration).
        public readonly ConcurrentBag<Serilog.Events.LogEvent> Captured = new();

        public void Emit(Serilog.Events.LogEvent logEvent)
        {
            Captured.Add(logEvent);
        }
    }
}
