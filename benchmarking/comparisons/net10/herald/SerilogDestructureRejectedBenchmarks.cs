#nullable enable

using BenchmarkDotNet.Attributes;
using MMP.Herald.Events;
using MMP.Herald.Quick;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow;

/// <summary>
/// Herald native — REJECTED "Serilog docs" destructure family.
///
/// <para>
/// Same <c>{@Position}</c> destructure templates and argument values as
/// <see cref="SerilogDestructureBenchmarks"/>, but the pipeline floor is
/// <c>warn</c> so every <c>Information</c> call lands below the floor and is
/// rejected. Production systems reject far more events than they accept — a
/// service running at <c>warn</c> still executes every <c>Information</c>
/// emit site on the hot path. The cost of a rejected structured call is
/// therefore what dominates in aggregate.
/// </para>
///
/// <para>
/// Herald's typed overloads take the arguments directly (no parameter array)
/// and the level-bound fast path resolves the rank comparison before any
/// argument is touched. A rejected call short-circuits before
/// <c>_position</c> is read or any int is boxed — zero allocation, single-digit
/// nanoseconds, regardless of arity. Method names match the serilog and
/// serilog-compat rows so <c>compare.sh --filter *Rejected_Arity*</c> runs the
/// full three-way family.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class SerilogDestructureRejectedBenchmarks
{
    // ── Pipeline ──────────────────────────────────────────────────────────
    private QuickLogResult _result = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Floor = warn: every Information call below is rejected at the gate.
        _result = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("warn")
            .BuildAndCommit();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_result.AsyncResource is { } resource)
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    // ── Inputs (identical to the accept family) ───────────────────────────
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

    // ── Benchmarks (below-floor: every call is rejected) ──────────────────

    [Benchmark(Description = "Herald rejected — {@Position} only (arity 1)")]
    public void Rejected_Arity1()
        => _result.Logger.Information(LogCategory.App,
            "Processed {@Position}.",
            _position);

    [Benchmark(Description = "Herald rejected — serilog.net docs example (arity 2)")]
    public void Rejected_Arity2()
        => _result.Logger.Information(LogCategory.App,
            "Processed {@Position} in {Elapsed:000} ms.",
            _position, _elapsed);

    [Benchmark(Description = "Herald rejected — {@Position} + 3 ints (arity 4)")]
    public void Rejected_Arity4()
        => _result.Logger.Information(LogCategory.App,
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt}.",
            _position, _elapsed, _requestId, _attempt);

    [Benchmark(Description = "Herald rejected — {@Position} + 7 ints (arity 8)")]
    public void Rejected_Arity8()
        => _result.Logger.Information(LogCategory.App,
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt} status {Status} queue {Queue} thread {Thread} batch {Batch}.",
            _position, _elapsed, _requestId, _attempt, _status, _queue, _thread, _batch);

    [Benchmark(Description = "Herald rejected — {@Position} + 11 ints (arity 12)")]
    public void Rejected_Arity12()
        => _result.Logger.Information(LogCategory.App,
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt} status {Status} queue {Queue} thread {Thread} batch {Batch} errors {Errors} duration {Duration} port {Port} page {Page}.",
            _position, _elapsed, _requestId, _attempt, _status, _queue, _thread, _batch,
            _errors, _duration, _port, _page);

    [Benchmark(Description = "Herald rejected — {@Position} + 15 ints (arity 16)")]
    public void Rejected_Arity16()
        => _result.Logger.Information(LogCategory.App,
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt} status {Status} queue {Queue} thread {Thread} batch {Batch} errors {Errors} duration {Duration} port {Port} page {Page} offset {Offset} total {Total} priority {Priority} cache {Cache}.",
            _position, _elapsed, _requestId, _attempt, _status, _queue, _thread, _batch,
            _errors, _duration, _port, _page, _offset, _total, _priority, _cache);
}
