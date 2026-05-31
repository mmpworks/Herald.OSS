#nullable enable

using BenchmarkDotNet.Attributes;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.SerilogRow;

/// <summary>
/// Real Serilog 4.3.1 — "Serilog docs" destructure family.
///
/// <para>
/// Mirror of <c>HeraldRow.SerilogDestructureBenchmarks</c>. Identical
/// templates and argument values across all three projects so the three-way
/// compare produces an apples-to-apples table. Method names are identical
/// to allow filtering with <c>--filter *Canonical_Arity*</c> across all
/// three projects in a single compare.sh run.
/// </para>
///
/// <para>
/// Serilog's <c>Information(template, params object?[]?)</c> signature
/// materialises an <c>object?[]</c> array at every call site. For the
/// <c>{@Position}</c> hole Serilog additionally reflects the anonymous type
/// and builds a <c>StructureValue</c> — two heap allocations on top of
/// the parameter array. The int arguments each box into a separate
/// <c>ScalarValue</c>. Total allocation grows with arity.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class SerilogDestructureBenchmarks
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

    [Benchmark(Description = "Serilog — {@Position} only (arity 1)")]
    public void Canonical_Arity1()
        => _logger.Information(
            "Processed {@Position}.",
            _position);

    // THE canonical example from https://serilog.net/
    [Benchmark(Description = "Serilog — serilog.net docs example (arity 2)")]
    public void Canonical_Arity2()
        => _logger.Information(
            "Processed {@Position} in {Elapsed:000} ms.",
            _position, _elapsed);

    [Benchmark(Description = "Serilog — {@Position} + 3 ints (arity 4)")]
    public void Canonical_Arity4()
        => _logger.Information(
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt}.",
            _position, _elapsed, _requestId, _attempt);

    [Benchmark(Description = "Serilog — {@Position} + 7 ints (arity 8)")]
    public void Canonical_Arity8()
        => _logger.Information(
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt} status {Status} queue {Queue} thread {Thread} batch {Batch}.",
            _position, _elapsed, _requestId, _attempt, _status, _queue, _thread, _batch);

    [Benchmark(Description = "Serilog — {@Position} + 11 ints (arity 12)")]
    public void Canonical_Arity12()
        => _logger.Information(
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt} status {Status} queue {Queue} thread {Thread} batch {Batch} errors {Errors} duration {Duration} port {Port} page {Page}.",
            _position, _elapsed, _requestId, _attempt, _status, _queue, _thread, _batch,
            _errors, _duration, _port, _page);

    [Benchmark(Description = "Serilog — {@Position} + 15 ints (arity 16)")]
    public void Canonical_Arity16()
        => _logger.Information(
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt} status {Status} queue {Queue} thread {Thread} batch {Batch} errors {Errors} duration {Duration} port {Port} page {Page} offset {Offset} total {Total} priority {Priority} cache {Cache}.",
            _position, _elapsed, _requestId, _attempt, _status, _queue, _thread, _batch,
            _errors, _duration, _port, _page, _offset, _total, _priority, _cache);

    private sealed class SerilogNullSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent) { }
    }
}
