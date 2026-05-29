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
    public void ZLogger_TwoProps()
    {
        var a = "alpha";
        var b = 7;
        _logger.ZLogInformation($"accept-two {a} {b}");
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

    [Benchmark]
    public void ZLogger_EightProps()
    {
        var a = "alpha";
        var b = 7;
        var c = true;
        var d = 3.14;
        var e = "beta";
        var f = 11;
        var g = false;
        var h = 2.71;
        _logger.ZLogInformation($"accept-eight {a} {b} {c} {d} {e} {f} {g} {h}");
    }

    [Benchmark]
    public void ZLogger_SixteenProps()
    {
        var a = "alpha";
        var b = 7;
        var c = true;
        var d = 3.14;
        var e = "beta";
        var f = 11;
        var g = false;
        var h = 2.71;
        var i = "gamma";
        var j = 13;
        var k = true;
        var l = 1.41;
        var m = "delta";
        var n = 17;
        var o = false;
        var p = 1.73;
        _logger.ZLogInformation(
            $"accept-sixteen {a} {b} {c} {d} {e} {f} {g} {h} {i} {j} {k} {l} {m} {n} {o} {p}");
    }
}
