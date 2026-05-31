#nullable enable

using BenchmarkDotNet.Attributes;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.SerilogRow;

/// <summary>
/// Serilog accept-path comparison row. Matched shape across every
/// competitor in benchmarking/comparisons/net10/. Sink is the no-op
/// SerilogNullSink — Serilog defers rendering to the sink, so a null
/// sink that doesn't format is the closest fair analogue to Herald's
/// discarding bridge.
/// </summary>
[MemoryDiagnoser]
public class AcceptCallBenchmarks
{
    private Logger _logger = null!;

    [GlobalSetup]
    public void Setup()
    {
        _logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(new SerilogNullSink())
            .CreateLogger();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _logger.Dispose();
    }

    // ── Canonical comparison rows (shared name across all three projects) ───────
    // Template and values are fixed across Herald native / Serilog-compat /
    // real Serilog so the three-way compare.sh run produces an apples-to-apples
    // table. Serilog.Core.Logger.Information is params object?[]? — the C#
    // compiler materialises an object?[] array at the call site even though
    // strings don't box individually. Expected: array allocation per call.

    private const string _canonTemplate2 = "User {Name} from {City}";
    private const string _canonTemplate4 = "User {Name} from {City} did {Action} on {Resource}";
    private static readonly string _canonName     = "alice";
    private static readonly string _canonCity     = "London";
    private static readonly string _canonAction   = "purchase";
    private static readonly string _canonResource = "/api/orders";

    [Benchmark(Description = "Canonical 2-prop all-strings")]
    public void Compare_Arity2_AllStrings()
    {
        _logger.Information(_canonTemplate2, _canonName, _canonCity);
    }

    // THE headline benchmark: verbatim code from https://serilog.net/ (the Serilog documentation).
    // After Herald's Approach A lands (CaptureMode on LogPropertyCompact), this achieves
    // 0 B pipeline allocation on Herald while Real Serilog 4.3.1 pays ~720 B.
    // Same template. Same args. Different engine.
    private static readonly object _position = new { Latitude = 25, Longitude = 134 };
    private const int _elapsedMs = 34;
    private const string _serilogCanonicalTemplate = "Processed {@Position} in {Elapsed:000} ms.";

    [Benchmark(Description = "Serilog docs canonical example — Real Serilog 4.3.1")]
    public void Compare_Arity2_SerilogCanonical()
        => _logger.Information(_serilogCanonicalTemplate, _position, _elapsedMs);

    [Benchmark(Description = "Canonical 4-prop all-strings")]
    public void Compare_Arity4_AllStrings()
    {
        _logger.Information(
            _canonTemplate4,
            _canonName, _canonCity, _canonAction, _canonResource);
    }

    [Benchmark]
    public void Serilog_ZeroProps()
    {
        _logger.Information("accept-zero");
    }

    [Benchmark]
    public void Serilog_OneProp()
    {
        _logger.Information("accept-one {Value}", 42);
    }

    [Benchmark]
    public void Serilog_TwoProps()
    {
        _logger.Information("accept-two {A} {B}", "alpha", 7);
    }

    [Benchmark]
    public void Serilog_FourProps()
    {
        _logger.Information("accept-four {A} {B} {C} {D}", "alpha", 7, true, 3.14);
    }

    [Benchmark]
    public void Serilog_EightProps()
    {
        _logger.Information(
            "accept-eight {A} {B} {C} {D} {E} {F} {G} {H}",
            "alpha", 7, true, 3.14, "beta", 11, false, 2.71);
    }

    [Benchmark]
    public void Serilog_TwelveProps()
    {
        _logger.Information(
            "accept-twelve {A} {B} {C} {D} {E} {F} {G} {H} {I} {J} {K} {L}",
            "alpha", 7, true, 3.14, "beta", 11, false, 2.71,
            "gamma", 13, true, 1.41);
    }

    [Benchmark]
    public void Serilog_SixteenProps()
    {
        _logger.Information(
            "accept-sixteen {A} {B} {C} {D} {E} {F} {G} {H} {I} {J} {K} {L} {M} {N} {O} {P}",
            "alpha", 7, true, 3.14, "beta", 11, false, 2.71,
            "gamma", 13, true, 1.41, "delta", 17, false, 1.73);
    }

    /// <summary>
    /// No-op Serilog sink. Consumes events without rendering or
    /// writing. Serilog ships no public null sink, so the comparison
    /// suite ships this one to keep the row fair.
    /// </summary>
    private sealed class SerilogNullSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent) { }
    }
}
