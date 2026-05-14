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
/// Per-call dispatch cost across every fan-out shape in
/// <see cref="KernelCompiler"/>. The compiler picks one of four
/// concrete shapes (Single / Pair / Triple / Many) based on sink
/// count; this bench measures all four plus several
/// <c>Many</c>-band arities (5 / 8 / 16) to show how the captured-
/// array loop scales as the sink set grows.
///
/// <para>
/// The key claim: Herald's compiled fan-out is roughly flat from
/// 1 to 16 sinks because the JIT keeps the unrolled sequences and
/// the captured-array loop in registers and the per-sink cost is
/// one virtual call through <see cref="IKernelSink"/>. Linear
/// scaling at this scale would be ~5 ns per added sink (the cost of
/// a sink's empty <c>Log(in buffer)</c> body); flat scaling shows
/// the JIT is doing the work.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class KernelFanOutBenchmarks
{
    private LogKernel _single = null!;
    private LogKernel _pair = null!;
    private LogKernel _triple = null!;
    private LogKernel _many5 = null!;
    private LogKernel _many8 = null!;
    private LogKernel _many16 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _single = KernelCompiler.CompileFanOut(MakeSinks(1));
        _pair = KernelCompiler.CompileFanOut(MakeSinks(2));
        _triple = KernelCompiler.CompileFanOut(MakeSinks(3));
        _many5 = KernelCompiler.CompileFanOut(MakeSinks(5));
        _many8 = KernelCompiler.CompileFanOut(MakeSinks(8));
        _many16 = KernelCompiler.CompileFanOut(MakeSinks(16));
    }

    [Benchmark(Baseline = true)]
    public void FanOut_Single() => Invoke(_single);

    [Benchmark]
    public void FanOut_Pair() => Invoke(_pair);

    [Benchmark]
    public void FanOut_Triple() => Invoke(_triple);

    [Benchmark]
    public void FanOut_Many_5() => Invoke(_many5);

    [Benchmark]
    public void FanOut_Many_8() => Invoke(_many8);

    [Benchmark]
    public void FanOut_Many_16() => Invoke(_many16);

    private static ILogger[] MakeSinks(int count)
    {
        var sinks = new ILogger[count];
        for (var i = 0; i < count; i++) sinks[i] = new NullKernelSink();
        return sinks;
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
