#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// Failure-isolation contract for the kernel fan-out shapes. A single
/// throwing sink must not stop its peers from receiving the event. When a
/// failure sink is wired into <see cref="KernelCompiler.CompileFanOut"/>,
/// the throw is routed through it as a synthesized <see cref="LogEvent"/>;
/// when no failure sink is wired, the throw falls back to
/// <see cref="Trace.WriteLine"/> with the <c>[Herald.OSS] kernel sink threw</c>
/// prefix so a TraceListener can surface it. <see cref="AuditLogFailureException"/>
/// is the one exception the kernel lets propagate — that contract is shared
/// with <c>SafeCompositeLogger</c>.
/// </summary>
public sealed class KernelFanOutFailureIsolationTests
{
    [Fact]
    public void Pair_throwing_sink_does_not_block_peer()
    {
        var bad = new ThrowingKernelSink(new InvalidOperationException("boom"));
        var good = new CapturingKernelSink();

        var kernel = KernelCompiler.CompileFanOut(new ILogger[] { bad, good });
        var listener = new CapturingTraceListener();
        Trace.Listeners.Add(listener);

        try
        {
            InvokeKernel(kernel, "pair-isolation");
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        good.Captured.Should().ContainSingle(m => m == "pair-isolation");
        listener.Messages.Should().Contain(s => s.Contains("[Herald.OSS] kernel sink threw"));
    }

    [Fact]
    public void Triple_throwing_first_sink_does_not_block_peers()
    {
        var bad = new ThrowingKernelSink(new InvalidOperationException("boom"));
        var b = new CapturingKernelSink();
        var c = new CapturingKernelSink();

        var kernel = KernelCompiler.CompileFanOut(new ILogger[] { bad, b, c });
        var listener = new CapturingTraceListener();
        Trace.Listeners.Add(listener);

        try
        {
            InvokeKernel(kernel, "triple-isolation");
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        b.Captured.Should().ContainSingle(m => m == "triple-isolation");
        c.Captured.Should().ContainSingle(m => m == "triple-isolation");
        listener.Messages.Should().Contain(s => s.Contains("[Herald.OSS] kernel sink threw"));
    }

    [Fact]
    public void Many_throwing_middle_sink_does_not_block_peers()
    {
        var sinks = new ILogger[]
        {
            new CapturingKernelSink(),
            new CapturingKernelSink(),
            new ThrowingKernelSink(new InvalidOperationException("boom")),
            new CapturingKernelSink(),
            new CapturingKernelSink(),
        };

        var kernel = KernelCompiler.CompileFanOut(sinks);
        var listener = new CapturingTraceListener();
        Trace.Listeners.Add(listener);

        try
        {
            InvokeKernel(kernel, "many-isolation");
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        ((CapturingKernelSink)sinks[0]).Captured.Should().ContainSingle(m => m == "many-isolation");
        ((CapturingKernelSink)sinks[1]).Captured.Should().ContainSingle(m => m == "many-isolation");
        ((CapturingKernelSink)sinks[3]).Captured.Should().ContainSingle(m => m == "many-isolation");
        ((CapturingKernelSink)sinks[4]).Captured.Should().ContainSingle(m => m == "many-isolation");
        listener.Messages.Should().Contain(s => s.Contains("[Herald.OSS] kernel sink threw"));
    }

    [Fact]
    public void Single_throwing_sink_does_not_propagate_to_caller()
    {
        var bad = new ThrowingKernelSink(new InvalidOperationException("boom"));
        var kernel = KernelCompiler.CompileFanOut(new ILogger[] { bad });

        Action act = () => InvokeKernel(kernel, "single-isolation");

        act.Should().NotThrow();
    }

    [Fact]
    public void Audit_failure_propagates_through_kernel()
    {
        var logEvent = new LogEvent(
            TimeUtc: DateTimeOffset.UtcNow,
            Level: KnownLogLevels.Information,
            Category: LogCategory.App,
            MessageTemplate: "audit",
            Message: "audit",
            Properties: Array.Empty<LogProperty>(),
            Context: LogEvent.EmptyContext);
        var auditException = new AuditLogFailureException(
            sinkName: "audit-sink",
            failedEvent: logEvent,
            innerException: new InvalidOperationException("inner"));
        var bad = new ThrowingKernelSink(auditException);
        var good = new CapturingKernelSink();

        var kernel = KernelCompiler.CompileFanOut(new ILogger[] { bad, good });

        Action act = () => InvokeKernel(kernel, "audit");

        act.Should().Throw<AuditLogFailureException>();
    }

    [Fact]
    public void Failure_sink_receives_synthesized_event_when_wired()
    {
        var bad = new ThrowingKernelSink(new InvalidOperationException("boom"));
        var good = new CapturingKernelSink();
        var failureSink = new CapturingFailureSink();

        var kernel = KernelCompiler.CompileFanOut(
            sinks: new ILogger[] { bad, good },
            enrichers: null,
            decorators: null,
            failureSink: failureSink);

        InvokeKernel(kernel, "failure-sink-wired");

        // Peer delivery is preserved.
        good.Captured.Should().ContainSingle(m => m == "failure-sink-wired");

        // The configured failure sink received the throw with the right
        // exception type, sink-type identification, and a synthesized event
        // carrying the buffer's template + level + category.
        failureSink.Failures.Should().ContainSingle();
        var entry = failureSink.Failures[0];
        entry.Exception.Should().BeOfType<InvalidOperationException>();
        entry.Source.Should().Be(nameof(ThrowingKernelSink));
        entry.Event.MessageTemplate.Should().Be("failure-sink-wired");
        entry.Event.Level.Should().Be(KnownLogLevels.Information);
        entry.Event.Category.Should().Be(LogCategory.App);
    }

    [Fact]
    public void Trace_fallback_fires_when_no_failure_sink_is_wired()
    {
        var bad = new ThrowingKernelSink(new InvalidOperationException("trace-only"));
        var good = new CapturingKernelSink();

        // No failureSink overload — falls through to Trace.WriteLine.
        var kernel = KernelCompiler.CompileFanOut(new ILogger[] { bad, good });
        var listener = new CapturingTraceListener();
        Trace.Listeners.Add(listener);

        try
        {
            InvokeKernel(kernel, "no-failure-sink");
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        good.Captured.Should().ContainSingle(m => m == "no-failure-sink");
        listener.Messages.Should().Contain(s => s.Contains("[Herald.OSS] kernel sink threw"));
    }

    private static void InvokeKernel(LogKernel kernel, string message)
    {
        var props = ReadOnlySpan<LogProperty>.Empty;
        var buffer = new LogEventBuffer(
            timeUtc: DateTimeOffset.UtcNow,
            level: KnownLogLevels.Information,
            category: LogCategory.App,
            messageTemplate: message,
            message: message,
            properties: props);

        kernel(in buffer);
    }

    private sealed class CapturingKernelSink : ILogger, IKernelSink
    {
        public ConcurrentBag<string> Captured { get; } = new();

        public void Log(in LogEventBuffer buffer) => Captured.Add(buffer.Message);

        public void Log(LogEvent logEvent) => Captured.Add(logEvent.Message);

        public ValueTask LogAsync(LogEvent logEvent, System.Threading.CancellationToken cancellationToken = default)
        {
            Captured.Add(logEvent.Message);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingKernelSink : ILogger, IKernelSink
    {
        private readonly Exception _exception;
        public ThrowingKernelSink(Exception exception) { _exception = exception; }

        public void Log(in LogEventBuffer buffer) => throw _exception;
        public void Log(LogEvent logEvent) => throw _exception;
        public ValueTask LogAsync(LogEvent logEvent, System.Threading.CancellationToken cancellationToken = default)
            => throw _exception;
    }

    private sealed class CapturingTraceListener : TraceListener
    {
        public ConcurrentBag<string> Messages { get; } = new();
        public override void Write(string? message) { if (message is not null) Messages.Add(message); }
        public override void WriteLine(string? message) { if (message is not null) Messages.Add(message); }
    }

    private sealed record FailureEntry(LogEvent Event, Exception Exception, string Source);

    private sealed class CapturingFailureSink : ILogFailureSink
    {
        public List<FailureEntry> Failures { get; } = new();

        public void ReportFailure(LogEvent logEvent, Exception exception, string source)
        {
            Failures.Add(new FailureEntry(logEvent, exception, source));
        }
    }
}
