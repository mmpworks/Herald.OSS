#nullable enable

using BenchmarkDotNet.Attributes;
using MMP.Herald.Quick;
// Type aliases prevent Serilog.* vs MMP.Herald.Serilog.* namespace ambiguity.
using HeraldSerilogAdapter = MMP.Herald.Serilog.SerilogLoggerAdapter;
using L1Log                = MMP.Herald.Serilog.Log;
using SerilogLog           = Serilog.Log;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.SerilogCompatRow;

/// <summary>
/// Reject-path cost for both the Layer-2 Surface and the Layer-1 FastPath.
///
/// <para>
/// <b>Pipeline gating.</b> Both setups configure a minimum level of Error so
/// Information calls are rejected. The level gate fires before any buffer or
/// box is materialized — both paths should measure ~0 B and near-noise ns.
/// </para>
///
/// <para>
/// <b>Surface_Reject.</b> Calls through <c>Serilog.Log.*</c> (Layer-2, params).
/// The level gate in Layer-2 routes through the L1 Herald adapter. Even the
/// params array construction is elided by the JIT when the gate is always false
/// at the observed call site (though this is not guaranteed — hence measuring).
/// </para>
///
/// <para>
/// <b>FastPath_Reject.</b> Calls the typed overload on
/// <see cref="HeraldSerilogAdapter"/> directly. The <c>IsInformationAcceptable</c>
/// guard fires before the typed buffer is populated — expected ~6 ns / 0 B.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class SerilogCompatRejectBenchmarks
{
    private readonly int _userId = 42;
    private readonly int _count  = 7;

    // Surface path: backed by a Herald pipeline gated at Error.
    // Routed through Serilog.Log.* (Layer-2 params facade).
    private QuickLogResult       _surfaceResult  = null!;
    private HeraldSerilogAdapter _surfaceAdapter = null!;

    // FastPath path: SerilogLoggerAdapter backed by a Herald pipeline
    // gated at Error level. Called via typed overloads (0 B when gated).
    private QuickLogResult       _fastResult = null!;
    private HeraldSerilogAdapter _adapter    = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Surface adapter: minimum level = Error so Information is rejected.
        _surfaceResult  = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("error")
            .BuildAndCommit();
        _surfaceAdapter = new HeraldSerilogAdapter(_surfaceResult.Logger);
        L1Log.Logger    = _surfaceAdapter;

        // FastPath pipeline: same minimum level = Error. Separate instance so
        // the static-facade rows and typed-overload rows don't share a slot.
        _fastResult = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("error")
            .BuildAndCommit();
        _adapter = new HeraldSerilogAdapter(_fastResult.Logger);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        L1Log.Logger = null!;

        if (_surfaceResult.AsyncResource is { } r1)
            r1.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (_fastResult.AsyncResource is { } r2)
            r2.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    // ── Surface reject ────────────────────────────────────────────────────────
    // Layer-2 params path; Information is below the Error floor.
    // Boxing + array allocation MAY be elided by the JIT when the gate
    // is always false; measured number is the truth.

    [Benchmark]
    public void Surface_Reject_ZeroProps()
    {
        SerilogLog.Information("rejected-info");
    }

    [Benchmark]
    public void Surface_Reject_TwoProps()
    {
        const string template = "user {Id} ordered {Count}";
        SerilogLog.Information(template, _userId, _count);
    }

    [Benchmark]
    public void Surface_Instance_Reject_TwoProps()
    {
        // Explicit params cast forces the params fallback path (not typed overload).
        const string template = "user {Id} ordered {Count}";
        _surfaceAdapter.Information(template, (object?[]?)new object?[] { _userId, _count });
    }

    // ── FastPath reject ───────────────────────────────────────────────────────
    // IsInformationAcceptable guard fires before the typed buffer is touched.
    // Expected: ~6 ns / 0 B.

    [Benchmark]
    public void FastPath_Reject_ZeroProps()
    {
        _adapter.Information("rejected-info");
    }

    [Benchmark]
    public void FastPath_Reject_TwoProps()
    {
        const string template = "user {Id} ordered {Count}";
        _adapter.Information(template, _userId, _count);
    }
}
