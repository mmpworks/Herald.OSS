#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// Kernel fan-out correctness. The four arities (1 / 2 / 3 / many) each
/// take a hand-written code path inside <see cref="KernelCompiler"/>;
/// these tests pin each path to "every sink saw the event exactly once".
/// </summary>
public sealed class KernelFanOutTests
{
    [Fact]
    public void Single_sink_receives_one_event()
    {
        var sink = new CapturingKernelSink();
        var kernel = KernelCompiler.CompileFanOut(new[] { (ILogger)sink });

        InvokeKernel(kernel, "single");

        sink.Captured.Should().HaveCount(1).And.ContainSingle(m => m == "single");
    }

    [Fact]
    public void Pair_sinks_each_receive_one_event()
    {
        var a = new CapturingKernelSink();
        var b = new CapturingKernelSink();
        var kernel = KernelCompiler.CompileFanOut(new ILogger[] { a, b });

        InvokeKernel(kernel, "pair");

        a.Captured.Should().ContainSingle(m => m == "pair");
        b.Captured.Should().ContainSingle(m => m == "pair");
    }

    [Fact]
    public void Triple_sinks_each_receive_one_event()
    {
        var a = new CapturingKernelSink();
        var b = new CapturingKernelSink();
        var c = new CapturingKernelSink();
        var kernel = KernelCompiler.CompileFanOut(new ILogger[] { a, b, c });

        InvokeKernel(kernel, "triple");

        a.Captured.Should().ContainSingle(m => m == "triple");
        b.Captured.Should().ContainSingle(m => m == "triple");
        c.Captured.Should().ContainSingle(m => m == "triple");
    }

    [Fact]
    public void Many_sinks_each_receive_one_event()
    {
        var sinks = new CapturingKernelSink[7];
        for (var i = 0; i < sinks.Length; i++) sinks[i] = new CapturingKernelSink();

        var kernel = KernelCompiler.CompileFanOut(sinks);

        InvokeKernel(kernel, "many");

        foreach (var sink in sinks)
        {
            sink.Captured.Should().ContainSingle(m => m == "many");
        }
    }

    [Fact]
    public void Zero_sinks_does_not_throw()
    {
        var kernel = KernelCompiler.CompileFanOut(Array.Empty<ILogger>());

        var act = () => InvokeKernel(kernel, "zero");

        act.Should().NotThrow();
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

        public void Log(in LogEventBuffer buffer)
        {
            Captured.Add(buffer.Message);
        }

        public void Log(LogEvent logEvent)
        {
            Captured.Add(logEvent.Message);
        }

        public ValueTask LogAsync(LogEvent logEvent, System.Threading.CancellationToken cancellationToken = default)
        {
            Captured.Add(logEvent.Message);
            return ValueTask.CompletedTask;
        }
    }
}
