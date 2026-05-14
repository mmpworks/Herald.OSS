#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Templating;

namespace MMP.Herald.OSS.Benchmarks;

/// <summary>
/// Per-call dispatch cost across the four hand-written fan-out shapes
/// in <see cref="KernelCompiler"/>. Pinning these numbers separately
/// from the full accept-path measures how much the fan-out shape
/// contributes — Single / Pair / Triple / Many show the per-sink
/// fixed cost as the arity grows.
/// </summary>
[MemoryDiagnoser]
public class KernelFanOutBenchmarks
{
    private LogKernel _single = null!;
    private LogKernel _pair = null!;
    private LogKernel _triple = null!;
    private LogKernel _many = null!;

    [GlobalSetup]
    public void Setup()
    {
        _single = KernelCompiler.CompileFanOut(new ILogger[] { new NullKernelSink() });
        _pair = KernelCompiler.CompileFanOut(new ILogger[] { new NullKernelSink(), new NullKernelSink() });
        _triple = KernelCompiler.CompileFanOut(new ILogger[] { new NullKernelSink(), new NullKernelSink(), new NullKernelSink() });

        var manySinks = new ILogger[5];
        for (var i = 0; i < manySinks.Length; i++) manySinks[i] = new NullKernelSink();
        _many = KernelCompiler.CompileFanOut(manySinks);
    }

    [Benchmark(Baseline = true)]
    public void FanOut_Single()
    {
        Invoke(_single);
    }

    [Benchmark]
    public void FanOut_Pair()
    {
        Invoke(_pair);
    }

    [Benchmark]
    public void FanOut_Triple()
    {
        Invoke(_triple);
    }

    [Benchmark]
    public void FanOut_Many_5()
    {
        Invoke(_many);
    }

    private static void Invoke(LogKernel kernel)
    {
        var props = ReadOnlySpan<LogProperty>.Empty;
        var buffer = new LogEventBuffer(
            timeUtc: DateTimeOffset.UtcNow,
            level: KnownLogLevels.Info,
            category: LogCategory.App,
            messageTemplate: "bench",
            message: "bench",
            properties: props);

        kernel(in buffer);
    }

    /// <summary>
    /// Discards its input. The bench measures dispatch cost, not sink
    /// behaviour, so the sink does nothing inside Log.
    /// </summary>
    private sealed class NullKernelSink : ILogger, IKernelSink
    {
        public void Log(in LogEventBuffer buffer) { }
        public void Log(LogEvent logEvent) { }
        public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}
