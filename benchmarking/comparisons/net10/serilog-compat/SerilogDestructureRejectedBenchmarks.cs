#nullable enable

using BenchmarkDotNet.Attributes;
using MMP.Herald.Quick;
using MMP.Herald.Serilog;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.SerilogCompatRow;

/// <summary>
/// Herald Serilog-compat adapter — REJECTED "Serilog docs" destructure family.
///
/// <para>
/// Mirror of <c>HeraldRow.SerilogDestructureRejectedBenchmarks</c> and
/// <c>SerilogRow.SerilogDestructureRejectedBenchmarks</c>. Same templates and
/// argument values; the pipeline floor is <c>warn</c> so every
/// <c>Information</c> call is rejected.
/// </para>
///
/// <para>
/// The receiver is typed as <see cref="SerilogLoggerAdapter"/> so the compiler
/// binds the typed-generic overloads rather than the <c>params object?[]?</c>
/// fallback. Those overloads take the arguments directly — no call-site array —
/// and the level-bound fast path gates the call before <c>_position</c> is read
/// or any int is boxed. A rejected call is allocation-free at every arity,
/// without the adopter writing a manual <c>IsEnabled</c> guard. This is the
/// drop-in win: existing Serilog call sites keep their exact syntax and stop
/// paying for rejected logs. Method names match the herald and serilog rows
/// (<c>--filter *Rejected_Arity*</c>).
/// </para>
/// </summary>
[MemoryDiagnoser]
public class SerilogDestructureRejectedBenchmarks
{
    private QuickLogResult _result = null!;
    private SerilogLoggerAdapter _adapter = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Floor = warn: every Information call below is rejected at the gate.
        _result = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("warn")
            .BuildAndCommit();

        _adapter = new SerilogLoggerAdapter(_result.Logger);
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

    [Benchmark(Description = "Herald compat rejected — {@Position} only (arity 1)")]
    public void Rejected_Arity1()
        => _adapter.Information(
            "Processed {@Position}.",
            _position);

    [Benchmark(Description = "Herald compat rejected — serilog.net docs example (arity 2)")]
    public void Rejected_Arity2()
        => _adapter.Information(
            "Processed {@Position} in {Elapsed:000} ms.",
            _position, _elapsed);

    [Benchmark(Description = "Herald compat rejected — {@Position} + 3 ints (arity 4)")]
    public void Rejected_Arity4()
        => _adapter.Information(
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt}.",
            _position, _elapsed, _requestId, _attempt);

    [Benchmark(Description = "Herald compat rejected — {@Position} + 7 ints (arity 8)")]
    public void Rejected_Arity8()
        => _adapter.Information(
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt} status {Status} queue {Queue} thread {Thread} batch {Batch}.",
            _position, _elapsed, _requestId, _attempt, _status, _queue, _thread, _batch);

    [Benchmark(Description = "Herald compat rejected — {@Position} + 11 ints (arity 12)")]
    public void Rejected_Arity12()
        => _adapter.Information(
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt} status {Status} queue {Queue} thread {Thread} batch {Batch} errors {Errors} duration {Duration} port {Port} page {Page}.",
            _position, _elapsed, _requestId, _attempt, _status, _queue, _thread, _batch,
            _errors, _duration, _port, _page);

    [Benchmark(Description = "Herald compat rejected — {@Position} + 15 ints (arity 16)")]
    public void Rejected_Arity16()
        => _adapter.Information(
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt} status {Status} queue {Queue} thread {Thread} batch {Batch} errors {Errors} duration {Duration} port {Port} page {Page} offset {Offset} total {Total} priority {Priority} cache {Cache}.",
            _position, _elapsed, _requestId, _attempt, _status, _queue, _thread, _batch,
            _errors, _duration, _port, _page, _offset, _total, _priority, _cache);
}
