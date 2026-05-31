#nullable enable

using BenchmarkDotNet.Attributes;
using MMP.Herald.Quick;
using MMP.Herald.Serilog;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.SerilogCompatRow;

/// <summary>
/// Layer-1 zero-alloc typed overloads on <see cref="SerilogLoggerAdapter"/> —
/// the future surface-target numbers once typed overloads are lifted to Layer-2.
///
/// <para>
/// <b>Receiver.</b> The variable is typed as <see cref="SerilogLoggerAdapter"/>
/// (not <c>Serilog.ILogger</c>). That is the key difference from the Surface
/// family. The C# compiler resolves the typed-generic overloads
/// (<c>Information&lt;T1,T2&gt;</c> etc.) rather than the <c>params object?[]?</c>
/// fallback. Zero boxing for string and int arguments; one box per value-type
/// element when a non-specialized struct is passed.
/// </para>
///
/// <para>
/// <b>Pipeline.</b> A <c>WithNullSink</c> pipeline at minimum level "verbose"
/// discards every event — the same null-sink shape the competitive benchmarks
/// and <c>SerilogHotPathAllocTests</c> use. The adapter is constructed via
/// <see cref="SerilogLoggerAdapter(MMP.Herald.Pipeline.StructuredLogger)"/> so
/// it does not own the pipeline lifetime (disposal is handled in GlobalCleanup
/// via the <see cref="QuickLogResult"/>).
/// </para>
///
/// <para>
/// <b>Template interning.</b> Every template is a compile-time <c>const string</c>.
/// The CLR interns string literals, so <c>SerilogTemplateHoleIndex</c> always
/// hits its cache in the measured window (no first-call parse cost). This
/// matches the R-ALLOC-PROBE guard.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class SerilogCompatFastPathBenchmarks
{
    // ── Field values kept in fields so the JIT cannot constant-fold them ─────
    private readonly int    _userId = 42;
    private readonly int    _count  = 7;
    private readonly string _name   = "alice";
    private readonly string _path   = "/api/v1/orders";
    private readonly bool   _flag   = true;
    private readonly double _ratio  = 3.14;

    private QuickLogResult _result = null!;
    private SerilogLoggerAdapter _adapter = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Null-sink pipeline at verbose level — same topology as
        // SerilogHotPathAllocTests and ZeroAllocContractTests.
        _result = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("verbose")
            .BuildAndCommit();

        // Wrap the StructuredLogger in the adapter. The adapter does not own the
        // lifetime here (external-ownership constructor) — cleanup disposes _result.
        _adapter = new SerilogLoggerAdapter(_result.Logger);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_result.AsyncResource is { } resource)
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    // ── Canonical comparison rows (shared name across all three projects) ───────
    // Template and values are fixed across Herald native / Serilog-compat /
    // real Serilog so the three-way compare.sh run produces an apples-to-apples
    // table. All args are strings (reference types) — typed-generic overload
    // routes them through LogPropertyCompact.From<string> with no boxing and
    // no array allocation. Expected: 0 B allocated.

    private const string _canonTemplate2 = "User {Name} from {City}";
    private const string _canonTemplate4 = "User {Name} from {City} did {Action} on {Resource}";
    private static readonly string _canonName     = "alice";
    private static readonly string _canonCity     = "London";
    private static readonly string _canonAction   = "purchase";
    private static readonly string _canonResource = "/api/orders";

    [Benchmark(Description = "Canonical 2-prop all-strings")]
    public void Compare_Arity2_AllStrings()
    {
        _adapter.Information(_canonTemplate2, _canonName, _canonCity);
    }

    [Benchmark(Description = "Canonical 4-prop all-strings")]
    public void Compare_Arity4_AllStrings()
    {
        _adapter.Information(_canonTemplate4, _canonName, _canonCity, _canonAction, _canonResource);
    }

    // ── One-property shapes ───────────────────────────────────────────────────

    [Benchmark]
    public void FastPath_OneProp_Int()
    {
        // Information<T1>: int routes through LogPropertyCompact.From<int>.
        // On net9+, int has a specialized inline path — 0 B expected.
        const string template = "user {Id}";
        _adapter.Information(template, _userId);
    }

    [Benchmark]
    public void FastPath_OneProp_String()
    {
        const string template = "user {Name}";
        _adapter.Information(template, _name);
    }

    // ── Two-property shapes ───────────────────────────────────────────────────

    [Benchmark]
    public void FastPath_TwoIntProps()
    {
        // Primary headline comparison vs Surface_Static_TwoIntProps.
        // Expected: ~26 ns / 0 B.
        const string template = "user {Id} ordered {Count}";
        _adapter.Information(template, _userId, _count);
    }

    [Benchmark]
    public void FastPath_TwoStringProps()
    {
        // Strings are reference types — no boxing at the object? boundary.
        // Expected: ~26 ns / 0 B.
        const string template = "user {Name} at {Path}";
        _adapter.Information(template, _name, _path);
    }

    // ── Four-property shapes ──────────────────────────────────────────────────

    [Benchmark]
    public void FastPath_FourProps_AllStrings()
    {
        const string template = "req {A} {B} {C} {D}";
        _adapter.Information(template, _name, _path, _name, _path);
    }

    [Benchmark]
    public void FastPath_FourProps_MixedTypes()
    {
        // Mixed: string, int, bool, double. Non-string values box.
        // Expected: non-zero B (one box per value-type arg).
        const string template = "req {A} {B} {C} {D}";
        _adapter.Information(template, _name, _userId, _flag, _ratio);
    }

    // ── Eight-property shapes ─────────────────────────────────────────────────

    [Benchmark]
    public void FastPath_EightProps_AllStrings()
    {
        const string template = "req {A} {B} {C} {D} {E} {F} {G} {H}";
        _adapter.Information(template, _name, _path, _name, _path, _name, _path, _name, _path);
    }

    [Benchmark]
    public void FastPath_EightProps_MixedTypes()
    {
        const string template = "req {A} {B} {C} {D} {E} {F} {G} {H}";
        _adapter.Information(template, _name, _userId, _flag, _ratio, _name, _userId, _flag, _ratio);
    }

    // ── Twelve-property shapes ────────────────────────────────────────────────
    // Arity 12: mirrors the TwelveProps rows in the Herald + Serilog comparison
    // projects. Anchors the 8→16 scaling claim on three data points.

    [Benchmark]
    public void FastPath_TwelveProps_AllStrings()
    {
        const string template = "req {A} {B} {C} {D} {E} {F} {G} {H} {I} {J} {K} {L}";
        _adapter.Information(template,
            _name, _path, _name, _path, _name, _path,
            _name, _path, _name, _path, _name, _path);
    }

    [Benchmark]
    public void FastPath_TwelveProps_MixedTypes()
    {
        const string template = "req {A} {B} {C} {D} {E} {F} {G} {H} {I} {J} {K} {L}";
        _adapter.Information(template,
            _name, _userId, _flag, _ratio,
            _name, _userId, _flag, _ratio,
            _name, _userId, _flag, _ratio);
    }

    // ── Sixteen-property shapes ───────────────────────────────────────────────

    [Benchmark]
    public void FastPath_SixteenProps_AllStrings()
    {
        const string template = "req {A} {B} {C} {D} {E} {F} {G} {H} {I} {J} {K} {L} {M} {N} {O} {P}";
        _adapter.Information(template,
            _name, _path, _name, _path, _name, _path, _name, _path,
            _name, _path, _name, _path, _name, _path, _name, _path);
    }

    [Benchmark]
    public void FastPath_SixteenProps_MixedTypes()
    {
        const string template = "req {A} {B} {C} {D} {E} {F} {G} {H} {I} {J} {K} {L} {M} {N} {O} {P}";
        _adapter.Information(template,
            _name, _userId, _flag, _ratio,
            _name, _userId, _flag, _ratio,
            _name, _userId, _flag, _ratio,
            _name, _userId, _flag, _ratio);
    }
}
