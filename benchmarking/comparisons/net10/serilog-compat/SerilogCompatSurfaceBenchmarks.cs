#nullable enable

using BenchmarkDotNet.Attributes;
using MMP.Herald.Quick;
// Use type aliases to keep the Layer-2 Serilog.* and Layer-1 MMP.Herald.Serilog.*
// namespaces unambiguous. A broad "using Serilog;" with MMP.Herald.Serilog also
// in scope causes the compiler to misresolve Serilog.Log.Logger's property type.
using HeraldSerilogAdapter = MMP.Herald.Serilog.SerilogLoggerAdapter;
using L1Log                = MMP.Herald.Serilog.Log;
using SerilogLog           = Serilog.Log;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.SerilogCompatRow;

/// <summary>
/// Layer-2 mirror cost through the params path — the honest "what does a real
/// Serilog corpus consumer pay today" number.
///
/// <para>
/// <b>Call surface.</b> Every row calls through <c>Serilog.Log.*</c> (static
/// facade) or a <c>Serilog.Core.Logger</c> instance — the types a real consumer
/// would write. Both are Layer-2 mirror types whose verbs are
/// <c>params object?[]?</c>. That means:
/// <list type="bullet">
///   <item>Every value-type argument boxes at the call site.</item>
///   <item>The runtime allocates a <c>object?[]</c> array to hold the args.</item>
/// </list>
/// This is expected and correct behaviour. The numbers here are the honest
/// Layer-2 cost, not a defect.
/// </para>
///
/// <para>
/// <b>Setup wiring.</b> <c>Serilog.Log.*</c> delegates to the Layer-1 ambient
/// slot (<c>MMP.Herald.Serilog.Log.Logger</c>). The setup wires the L1 slot
/// directly via a <see cref="HeraldSerilogAdapter"/> backed by a null-sink
/// pipeline so <c>Serilog.Log.Information(...)</c> routes through the Herald
/// engine — the same engine the FastPath rows use.
/// </para>
///
/// <para>
/// <b>Comparison with FastPath family.</b> The zero-alloc typed overloads live
/// on <see cref="HeraldSerilogAdapter"/> (Layer-1 concrete type). They are not
/// accessible via <c>Serilog.Log.*</c> or <c>Serilog.ILogger</c>. The Surface
/// rows establish what Layer-2 costs; FastPath rows establish what Layer-1 costs.
/// The gap is the adoption-cost claim.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class SerilogCompatSurfaceBenchmarks
{
    // ── Field values kept in fields so the JIT cannot constant-fold them ─────
    private readonly int    _userId = 42;
    private readonly int    _count  = 7;
    private readonly string _name   = "alice";
    private readonly string _path   = "/api/v1/orders";
    private readonly bool   _flag   = true;
    private readonly double _ratio  = 3.14;

    private QuickLogResult       _result  = null!;
    private HeraldSerilogAdapter _adapter = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Null-sink pipeline at verbose level — identical topology to FastPath.
        _result  = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("verbose")
            .BuildAndCommit();

        // Wire the adapter into the Layer-1 ambient slot.
        // Serilog.Log.Information(...) forwards to L1.Log.Logger, so this
        // makes the static facade rows exercise the same Herald engine.
        _adapter = new HeraldSerilogAdapter(_result.Logger);
        L1Log.Logger = _adapter;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Reset the L1 ambient slot before tearing down the pipeline.
        L1Log.Logger = null!;

        if (_result.AsyncResource is { } resource)
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    // ── Static facade rows (Serilog.Log.*) ───────────────────────────────────
    // These are what a real Serilog consumer writes at the call site.
    // Serilog.Log.* delegates to MMP.Herald.Serilog.Log.Logger (L1 ambient slot),
    // which is the SerilogLoggerAdapter wired above.
    // params path → boxing + array allocation expected.

    // R-ALLOC-PROBE guard: template must be a compile-time const so the
    // hole-index cache is always warm in the measured window (no first-call
    // parse overhead polluting the steady-state number).

    [Benchmark]
    public void Surface_Static_OneProp()
    {
        const string template = "user {Id}";
        SerilogLog.Information(template, _userId);
    }

    [Benchmark]
    public void Surface_Static_TwoIntProps()
    {
        const string template = "user {Id} ordered {Count}";
        SerilogLog.Information(template, _userId, _count);
    }

    [Benchmark]
    public void Surface_Static_TwoStringProps()
    {
        const string template = "user {Name} at {Path}";
        SerilogLog.Information(template, _name, _path);
    }

    [Benchmark]
    public void Surface_Static_FourProps_AllStrings()
    {
        const string template = "req {A} {B} {C} {D}";
        SerilogLog.Information(template, _name, _path, _name, _path);
    }

    [Benchmark]
    public void Surface_Static_FourProps_MixedTypes()
    {
        const string template = "req {A} {B} {C} {D}";
        SerilogLog.Information(template, _name, _userId, _flag, _ratio);
    }

    [Benchmark]
    public void Surface_Static_EightProps_AllStrings()
    {
        const string template = "req {A} {B} {C} {D} {E} {F} {G} {H}";
        SerilogLog.Information(template, _name, _path, _name, _path, _name, _path, _name, _path);
    }

    [Benchmark]
    public void Surface_Static_EightProps_MixedTypes()
    {
        const string template = "req {A} {B} {C} {D} {E} {F} {G} {H}";
        SerilogLog.Information(template, _name, _userId, _flag, _ratio, _name, _userId, _flag, _ratio);
    }

    [Benchmark]
    public void Surface_Static_TwelveProps_AllStrings()
    {
        const string template = "req {A} {B} {C} {D} {E} {F} {G} {H} {I} {J} {K} {L}";
        SerilogLog.Information(template,
            _name, _path, _name, _path, _name, _path,
            _name, _path, _name, _path, _name, _path);
    }

    [Benchmark]
    public void Surface_Static_TwelveProps_MixedTypes()
    {
        const string template = "req {A} {B} {C} {D} {E} {F} {G} {H} {I} {J} {K} {L}";
        SerilogLog.Information(template,
            _name, _userId, _flag, _ratio,
            _name, _userId, _flag, _ratio,
            _name, _userId, _flag, _ratio);
    }

    [Benchmark]
    public void Surface_Static_SixteenProps_AllStrings()
    {
        const string template = "req {A} {B} {C} {D} {E} {F} {G} {H} {I} {J} {K} {L} {M} {N} {O} {P}";
        SerilogLog.Information(template,
            _name, _path, _name, _path, _name, _path, _name, _path,
            _name, _path, _name, _path, _name, _path, _name, _path);
    }

    [Benchmark]
    public void Surface_Static_SixteenProps_MixedTypes()
    {
        const string template = "req {A} {B} {C} {D} {E} {F} {G} {H} {I} {J} {K} {L} {M} {N} {O} {P}";
        SerilogLog.Information(template,
            _name, _userId, _flag, _ratio,
            _name, _userId, _flag, _ratio,
            _name, _userId, _flag, _ratio,
            _name, _userId, _flag, _ratio);
    }

    // ── Instance rows via the adapter (Layer-1 typed, params path) ───────────
    // Call the params verb directly on the adapter — same boxing cost as the
    // static facade (params still fires) but no extra ambient-slot indirection.
    // Labeled "Instance" to match the design doc's Surface_Instance terminology.

    [Benchmark]
    public void Surface_Instance_TwoIntProps()
    {
        const string template = "user {Id} ordered {Count}";
        // Explicit params cast forces the params fallback path, not the typed overload.
        _adapter.Information(template, (object?[]?)new object?[] { _userId, _count });
    }

    [Benchmark]
    public void Surface_Instance_FourProps_MixedTypes()
    {
        const string template = "req {A} {B} {C} {D}";
        _adapter.Information(template, (object?[]?)new object?[] { _name, _userId, _flag, _ratio });
    }
}
