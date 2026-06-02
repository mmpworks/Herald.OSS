#nullable enable

using System;
using System.Collections.Concurrent;
using global::Serilog;
using global::Serilog.Core;
using global::Serilog.Events;

namespace MMP.Herald.OSS.Tests.TestSupport;

/// <summary>
/// Parity oracle for G-VM.* Serilog-compat tests.
/// Runs the SAME logging call through real Serilog 4.3.1 and through
/// the MMP.Herald.Serilog shim, and provides both captured events for
/// canonical-shape comparison.
/// </summary>
public sealed class SerilogParityOracle
{
    // -- Real-Serilog half (fully functional since Task 1) --

    public static global::Serilog.Events.LogEvent CaptureSerilog(
        Action<global::Serilog.ILogger> log)
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
                "SerilogParityOracle.CaptureSerilog: no events. " +
                "Ensure the action logs at a level that passes MinimumLevel.Verbose()."),
            1 => events[0],
            _ => throw new InvalidOperationException(
                $"SerilogParityOracle.CaptureSerilog: expected 1 event, got {events.Length}. " +
                "Wrap a single log call per CaptureSerilog() invocation.")
        };
    }
    // -- Shim half (implemented in Task 8). Runs on all TFMs: the
    //    MMP.Herald.Serilog surface ships in Herald.OSS and is linked on net8. --

    public static MMP.Herald.Serilog.Events.LogEvent CaptureShim(
        Action<MMP.Herald.Serilog.ILogger> log)
    {
        if (log is null) throw new ArgumentNullException(nameof(log));

        var (herald, sink) = TestLoggers.CreateCapturing<SerilogParityOracle>();
        var adapter = new MMP.Herald.Serilog.SerilogLoggerAdapter(herald);
        log(adapter);

        var events = sink.GetEvents();
        return events.Count switch
        {
            0 => throw new InvalidOperationException(
                "SerilogParityOracle.CaptureShim: no events captured."),
            1 => new MMP.Herald.Serilog.Events.LogEvent(events[0]),
            _ => throw new InvalidOperationException(
                $"SerilogParityOracle.CaptureShim: expected 1 event, got {events.Count}.")
        };
    }

    // -- Private capture sink --

    private sealed class CapturingSink : ILogEventSink
    {
        public readonly ConcurrentBag<global::Serilog.Events.LogEvent> Captured = new();
        public void Emit(global::Serilog.Events.LogEvent logEvent) => Captured.Add(logEvent);
    }
}
