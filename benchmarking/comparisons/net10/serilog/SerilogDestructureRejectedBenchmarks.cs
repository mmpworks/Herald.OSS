#nullable enable

using BenchmarkDotNet.Attributes;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.SerilogRow;

/// <summary>
/// Real Serilog 4.3.1 — REJECTED "Serilog docs" destructure family.
///
/// <para>
/// Mirror of <c>HeraldRow.SerilogDestructureRejectedBenchmarks</c>. Same
/// templates and argument values; the logger floor is <c>Warning</c> so every
/// <c>Information</c> call is below the floor.
/// </para>
///
/// <para>
/// Serilog's <c>Information(template, params object?[]?)</c> signature
/// materialises the <c>object?[]</c> array — and boxes each int into it — at
/// the call site, <em>before</em> control enters <c>Information</c> and the
/// level gate runs. A rejected call therefore still pays the array + boxing
/// allocation unless the adopter hand-writes an <c>IsEnabled</c> guard around
/// it. This row measures the natural, unguarded call so the rejected-path cost
/// is apples-to-apples with the Herald rows (same method names →
/// <c>--filter *Rejected_Arity*</c>). The guarded pattern is covered by
/// <c>RejectedCallBenchmarks.*_Guarded</c>.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class SerilogDestructureRejectedBenchmarks
{
    private Logger _logger = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Floor = Warning: Information calls below are gated out — after the
        // params array has already been built at the call site.
        _logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .WriteTo.Sink(new SerilogNullSink())
            .CreateLogger();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _logger.Dispose();
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

    [Benchmark(Description = "Serilog rejected — {@Position} only (arity 1)")]
    public void Rejected_Arity1()
        => _logger.Information(
            "Processed {@Position}.",
            _position);

    [Benchmark(Description = "Serilog rejected — serilog.net docs example (arity 2)")]
    public void Rejected_Arity2()
        => _logger.Information(
            "Processed {@Position} in {Elapsed:000} ms.",
            _position, _elapsed);

    [Benchmark(Description = "Serilog rejected — {@Position} + 3 ints (arity 4)")]
    public void Rejected_Arity4()
        => _logger.Information(
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt}.",
            _position, _elapsed, _requestId, _attempt);

    [Benchmark(Description = "Serilog rejected — {@Position} + 7 ints (arity 8)")]
    public void Rejected_Arity8()
        => _logger.Information(
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt} status {Status} queue {Queue} thread {Thread} batch {Batch}.",
            _position, _elapsed, _requestId, _attempt, _status, _queue, _thread, _batch);

    [Benchmark(Description = "Serilog rejected — {@Position} + 11 ints (arity 12)")]
    public void Rejected_Arity12()
        => _logger.Information(
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt} status {Status} queue {Queue} thread {Thread} batch {Batch} errors {Errors} duration {Duration} port {Port} page {Page}.",
            _position, _elapsed, _requestId, _attempt, _status, _queue, _thread, _batch,
            _errors, _duration, _port, _page);

    [Benchmark(Description = "Serilog rejected — {@Position} + 15 ints (arity 16)")]
    public void Rejected_Arity16()
        => _logger.Information(
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt} status {Status} queue {Queue} thread {Thread} batch {Batch} errors {Errors} duration {Duration} port {Port} page {Page} offset {Offset} total {Total} priority {Priority} cache {Cache}.",
            _position, _elapsed, _requestId, _attempt, _status, _queue, _thread, _batch,
            _errors, _duration, _port, _page, _offset, _total, _priority, _cache);

    private sealed class SerilogNullSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent) { }
    }
}
