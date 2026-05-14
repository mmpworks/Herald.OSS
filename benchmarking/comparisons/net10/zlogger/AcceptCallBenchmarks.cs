#nullable enable

using System.IO;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.ZLoggerRow;

/// <summary>
/// ZLogger accept-path comparison row. Matched shape across every
/// competitor in benchmarking/comparisons/net10/. Sink is
/// <see cref="Stream.Null"/> via <c>AddZLoggerStream</c> — ZLogger's
/// idiomatic discarding pattern. ZLogger renders to bytes end-to-end,
/// so the null-stream sink still pays the format cost; that's how
/// ZLogger ships and the comparison should reflect it.
/// </summary>
[MemoryDiagnoser]
public class AcceptCallBenchmarks
{
    private ILoggerFactory _factory = null!;
    private ILogger _logger = null!;

    [GlobalSetup]
    public void Setup()
    {
        _factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddZLoggerStream(Stream.Null);
        });
        _logger = _factory.CreateLogger("bench");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _factory.Dispose();
    }

    [Benchmark]
    public void ZLogger_ZeroProps()
    {
        _logger.ZLogInformation($"accept-zero");
    }

    [Benchmark]
    public void ZLogger_OneProp()
    {
        var value = 42;
        _logger.ZLogInformation($"accept-one {value}");
    }

    [Benchmark]
    public void ZLogger_FourProps()
    {
        var a = "alpha";
        var b = 7;
        var c = true;
        var d = 3.14;
        _logger.ZLogInformation($"accept-four {a} {b} {c} {d}");
    }
}
