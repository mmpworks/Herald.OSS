#nullable enable

using BenchmarkDotNet.Attributes;
using MMP.Herald.Events;
using MMP.Herald.Quick;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow;

/// <summary>
/// Herald native — "Serilog docs" destructure family.
///
/// <para>
/// All benchmarks share a single <c>{@Position}</c> destructure hole as the
/// first argument, matching the canonical example from https://serilog.net/:
/// <code>
///   var position = new { Latitude = 25, Longitude = 134 };
///   var elapsedMs = 34;
///   log.Information("Processed {@Position} in {Elapsed:000} ms.", position, elapsedMs);
/// </code>
/// Additional properties are plain <c>int</c> values (request IDs, counters,
/// timings) — the realistic shape of a structured log call in production.
/// </para>
///
/// <para>
/// <b>Why <c>position</c> allocates zero bytes on Herald's pipeline.</b>
/// <c>position</c> is an anonymous-type instance already on the managed heap.
/// Passing a reference through <c>LogPropertyCompact.From&lt;object&gt;</c>
/// stores the reference directly in the <c>Value</c> field — no new object is
/// created in the pipeline. The <c>{@}</c> prefix sets
/// <c>CaptureMode.Destructure</c> on the compact slot; the destructure
/// instruction travels through the pipeline without materialising a
/// <c>StructureValue</c> wrapper.
/// </para>
///
/// <para>
/// Method names are identical across all three comparison projects
/// (herald / serilog / serilog-compat) so <c>compare.sh</c> can run
/// the family with a single filter: <c>--filter *Canonical_Arity*</c>.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class SerilogDestructureBenchmarks
{
    // ── Pipeline ──────────────────────────────────────────────────────────
    private QuickLogResult _result = null!;

    [GlobalSetup]
    public void Setup()
    {
        _result = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("trace")
            .BuildAndCommit();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_result.AsyncResource is { } resource)
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    // ── Inputs ────────────────────────────────────────────────────────────
    // position: anonymous-type class instance already on the heap.
    // Typed as object so T=object at the typed-overload boundary — no boxing.
    private static readonly object _position = new { Latitude = 25, Longitude = 134 };

    // Extra properties: plain ints. Kept in instance fields so the JIT
    // cannot constant-fold them away (same discipline as the other bench families).
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

    [Benchmark(Description = "Herald — {@Position} only (arity 1)")]
    public void Canonical_Arity1()
        => _result.Logger.Information(LogCategory.App,
            "Processed {@Position}.",
            _position);

    // THE canonical example from https://serilog.net/ — identical template and
    // values across all three projects.
    [Benchmark(Description = "Herald — serilog.net docs example (arity 2)")]
    public void Canonical_Arity2()
        => _result.Logger.Information(LogCategory.App,
            "Processed {@Position} in {Elapsed:000} ms.",
            _position, _elapsed);

    [Benchmark(Description = "Herald — {@Position} + 3 ints (arity 4)")]
    public void Canonical_Arity4()
        => _result.Logger.Information(LogCategory.App,
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt}.",
            _position, _elapsed, _requestId, _attempt);

    [Benchmark(Description = "Herald — {@Position} + 7 ints (arity 8)")]
    public void Canonical_Arity8()
        => _result.Logger.Information(LogCategory.App,
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt} status {Status} queue {Queue} thread {Thread} batch {Batch}.",
            _position, _elapsed, _requestId, _attempt, _status, _queue, _thread, _batch);

    [Benchmark(Description = "Herald — {@Position} + 11 ints (arity 12)")]
    public void Canonical_Arity12()
        => _result.Logger.Information(LogCategory.App,
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt} status {Status} queue {Queue} thread {Thread} batch {Batch} errors {Errors} duration {Duration} port {Port} page {Page}.",
            _position, _elapsed, _requestId, _attempt, _status, _queue, _thread, _batch,
            _errors, _duration, _port, _page);

    [Benchmark(Description = "Herald — {@Position} + 15 ints (arity 16)")]
    public void Canonical_Arity16()
        => _result.Logger.Information(LogCategory.App,
            "Processed {@Position} in {Elapsed:000} ms. Request {RequestId} attempt {Attempt} status {Status} queue {Queue} thread {Thread} batch {Batch} errors {Errors} duration {Duration} port {Port} page {Page} offset {Offset} total {Total} priority {Priority} cache {Cache}.",
            _position, _elapsed, _requestId, _attempt, _status, _queue, _thread, _batch,
            _errors, _duration, _port, _page, _offset, _total, _priority, _cache);
}
