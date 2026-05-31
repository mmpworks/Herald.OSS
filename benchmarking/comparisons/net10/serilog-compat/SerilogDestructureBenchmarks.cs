#nullable enable

using BenchmarkDotNet.Attributes;
using MMP.Herald.Quick;
using MMP.Herald.Serilog;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.SerilogCompatRow;

/// <summary>
/// Herald Serilog-compat adapter — "Serilog docs" destructure family.
///
/// <para>
/// Mirror of <c>HeraldRow.SerilogDestructureBenchmarks</c> and
/// <c>SerilogRow.SerilogDestructureBenchmarks</c>. Identical templates and
/// argument values across all three projects so the three-way compare
/// produces an apples-to-apples table.
/// </para>
///
/// <para>
/// The receiver is typed as <see cref="SerilogLoggerAdapter"/> so the C#
/// compiler resolves the typed-generic overloads
/// (<c>Information&lt;T1,T2&gt;</c> etc.) rather than the
/// <c>params object?[]?</c> fallback. The <c>{@Position}</c> hole sets
/// <c>CaptureMode.Destructure</c> on the compact slot via
/// <c>SerilogTemplateHoleIndex</c>; no <c>StructureValue</c> is created in
/// the pipeline. The int arguments route through
/// <c>LogPropertyCompact.From&lt;int&gt;</c> — one box per int on the
/// managed heap, but no parameter array and no per-event wrapper objects.
/// </para>
///
/// <para>
/// Method names match <c>HeraldRow</c> and <c>SerilogRow</c> exactly so
/// <c>compare.sh --filter *Canonical_Arity*</c> runs the full family.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class SerilogDestructureBenchmarks
{
    private QuickLogResult _result = null!;
    private SerilogLoggerAdapter _adapter = null!;

    [GlobalSetup]
    public void Setup()
    {
        _result = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("verbose")
            .BuildAndCommit();

        _adapter = new SerilogLoggerAdapter(_result.Logger);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_result.AsyncResource is { } resource)
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    // ── Inputs ────────────────────────────────────────────────────────────
    private static readonly object _position = new { Latitude = 25, Longitude = 134 };

    private readonly int _elapsed    = 34;
    private readonly int _requestId  = 42;
    private readonly int _attempt    = 3;
    private readonly int _status     = 200;
    private readonly int _queue      = 7;
    private readonly int _thread     = 14;
    private readonly int _batch      = 100;
    private readonly int _errors     = 0;
    private readonly int _duration   = 128;
    private readonly int _port       = 5432;
    private readonly int _page       = 20;
    private readonly int _offset     = 0;
    private readonly int _total      = 1500;
    private readonly int _priority   = 1;
    private readonly int _cache      = 0;

    // ── Benchmarks ────────────────────────────────────────────────────────

    [Benchmark(Description = "Herald compat — {@Position} only (arity 1)")]
    public void Canonical_Arity1()
        => _adapter.Information(
            "Processed {@Position}.",
            _position);

    // THE canonical example from https://serilog.net/
    [Benchmark(Description = "Herald compat — serilog.net docs example (arity 2)")]
    public void Canonical_Arity2()
        => _adapter.Information(
            "Processed {@Position} in {Elapsed:000} ms.",
            _position, _elapsed);

    [Benchmark(Description = "Herald compat — {@Position} + 3 ints (arity 4)")]
    public void Canonical_Arity4()
        => _adapter.Information(
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt}.",
            _position, _elapsed, _requestId, _attempt);

    [Benchmark(Description = "Herald compat — {@Position} + 7 ints (arity 8)")]
    public void Canonical_Arity8()
        => _adapter.Information(
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt} status {Status} queue {Queue} thread {Thread} batch {Batch}.",
            _position, _elapsed, _requestId, _attempt, _status, _queue, _thread, _batch);

    [Benchmark(Description = "Herald compat — {@Position} + 11 ints (arity 12)")]
    public void Canonical_Arity12()
        => _adapter.Information(
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt} status {Status} queue {Queue} thread {Thread} batch {Batch} errors {Errors} duration {Duration} port {Port} page {Page}.",
            _position, _elapsed, _requestId, _attempt, _status, _queue, _thread, _batch,
            _errors, _duration, _port, _page);

    [Benchmark(Description = "Herald compat — {@Position} + 15 ints (arity 16)")]
    public void Canonical_Arity16()
        => _adapter.Information(
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt} status {Status} queue {Queue} thread {Thread} batch {Batch} errors {Errors} duration {Duration} port {Port} page {Page} offset {Offset} total {Total} priority {Priority} cache {Cache}.",
            _position, _elapsed, _requestId, _attempt, _status, _queue, _thread, _batch,
            _errors, _duration, _port, _page, _offset, _total, _priority, _cache);
}
