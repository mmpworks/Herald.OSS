#nullable enable
#if NET9_0_OR_GREATER

using System;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Quick;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Events;
using Xunit;

// Guard 2 tests run in a dedicated serialized collection to prevent the static
// ProjectionCallCount from being corrupted by parallel test execution.
[assembly: CollectionBehavior(DisableTestParallelization = false)]

namespace MMP.Herald.OSS.Tests.Serilog;

/// <summary>
/// Guard 2 + G-HOT.1: proves the native Herald path through
/// <see cref="SerilogLoggerAdapter"/> allocates zero bytes when the mirror
/// assembly (<c>MMP.Herald.Serilog</c>) is loaded but never asked to project.
///
/// <para>
/// Guard 2 is the contract that the Serilog-compat layer costs nothing unless a
/// custom extension explicitly reads <see cref="LogEvent.Properties"/>.
/// The native path writes directly into the Herald pipeline and never touches
/// <c>LogEventValueProjector.Project</c>.
/// </para>
///
/// <para>
/// <b>Primary gate (allocation probe).</b> Uses a null-sink pipeline — the same
/// shape <c>ZeroAllocContractTests</c> and <c>SerilogHotPathAllocTests</c> use.
/// <c>WithNullSink</c> discards events without storing a heap <c>LogEvent</c>,
/// so the only allocation in the measured window is the adapter call path itself.
/// On the native pass-through path that cost is zero.
/// </para>
///
/// <para>
/// <b>Secondary diagnostic (call-count probe).</b> Pairs a
/// zero-count native-path assertion with a positive control (count == 1 when
/// a Serilog enricher explicitly reads <c>.Properties</c>). Without the positive
/// control a broken counter passes the zero assertion vacuously. The counter
/// lives on <c>LogEventValueProjector</c> behind the existing
/// <c>InternalsVisibleTo("Herald.OSS.Tests")</c> grant.
/// </para>
///
/// <para>
/// <b>Parallelization.</b> The call-count tests use a static counter and must
/// not run concurrently with each other. The <c>Guard2Collection</c> xUnit
/// collection disables parallelization within this group while leaving all other
/// collections unaffected.
/// </para>
///
/// <para>Gated to net9.0+: <c>MMP.Herald.Serilog</c> does not target net8.</para>
/// </summary>
[Collection("Guard2")]
public sealed class Guard2_NativePathZeroAllocTests : IDisposable
{
    // Null-sink pipeline at verbose minimum: same shape as ZeroAllocContractTests.
    // WithNullSink means no per-event LogEvent is stored on the heap — the only
    // allocation in the measured window is what the adapter call path does itself.
    private readonly QuickLogResult _nullSinkVerbose;

    public Guard2_NativePathZeroAllocTests()
    {
        _nullSinkVerbose = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("verbose")
            .BuildAndCommit();
    }

    public void Dispose()
    {
        if (_nullSinkVerbose.AsyncResource is { } resource)
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    // ── Primary gate: native path allocates 0 B when mirror assembly is loaded ─

    /// <summary>
    /// G-HOT.1: logging through <see cref="SerilogLoggerAdapter"/> with a
    /// null-sink pipeline allocates zero bytes, even though the mirror assembly
    /// (<c>MMP.Herald.Serilog</c>) is loaded in the same process.
    ///
    /// The mirror being loaded is the relevant condition: a naive implementation
    /// that instantiated the mirror <see cref="LogEvent"/> on every log call
    /// would allocate here. The correct implementation never touches the mirror
    /// on the hot path.
    /// </summary>
    [Fact]
    public void Native_sink_path_allocates_zero_bytes_with_mirror_loaded()
    {
        var adapter = new SerilogLoggerAdapter(_nullSinkVerbose.Logger);
        const string template = "user {Id}"; // compile-time literal, CLR-interned

        // AllocationProbe warms the JIT and the per-template hole-index cache
        // before measuring, so first-call costs stay outside the window.
        var bytes = AllocationProbe.BytesPerIteration(
            () => adapter.Information(template, 7));

        bytes.Should().Be(0,
            "the native path through SerilogLoggerAdapter must not allocate " +
            "even when the mirror assembly is loaded in the same process. " +
            "A non-zero result means the mirror LogEvent is being constructed " +
            "on every log call — Guard 2 is broken.");
    }

    /// <summary>
    /// Reject path: when the level gate short-circuits (Information below Error
    /// minimum), the adapter returns before any buffer or property touch.
    /// Zero bytes regardless of mirror assembly presence.
    /// </summary>
    [Fact]
    public void Reject_path_allocates_zero_bytes()
    {
        // Separate pipeline gated at Error so Information is rejected immediately.
        var rejecting = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("error")
            .BuildAndCommit();

        try
        {
            var adapter = new SerilogLoggerAdapter(rejecting.Logger);
            const string template = "user {Id}";

            var bytes = AllocationProbe.BytesPerIteration(
                () => adapter.Information(template, 7));

            bytes.Should().Be(0,
                "the level-gate guard returns before any buffer; " +
                "even the params-array path is never reached");
        }
        finally
        {
            if (rejecting.AsyncResource is { } resource)
                resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    // ── Secondary diagnostic: call-count probe + positive control ────────────

    /// <summary>
    /// Positive control: proves <c>ProjectionCallCount</c> increments when a
    /// Serilog enricher explicitly reads <c>.Properties</c> on the mirror event.
    ///
    /// Without this control, a broken counter (e.g. always zero) would pass the
    /// zero-count native-path assertion vacuously. The control locks the counter
    /// to exactly one for a single projection read.
    /// </summary>
    [Fact]
    public void Custom_extension_reading_Properties_triggers_projection_exactly_once()
    {
        // Arrange: capture one native event through a capturing pipeline.
        // TestLoggers.CreateCapturing uses InMemoryLogSink (allocating), which is
        // acceptable here — this is the positive control, not the alloc gate.
        var (heraldLogger, sink) = TestLoggers.CreateCapturing<Guard2_NativePathZeroAllocTests>();
        var adapter = new SerilogLoggerAdapter(heraldLogger);
        adapter.Information("x {V}", 42);

        var captured = sink.GetEvents();
        captured.Should().HaveCount(1, "one log call must produce one captured event");
        var nativeEvent = captured[0];

        // Reset so the act phase starts from zero.
        LogEventValueProjector.ResetCallCount();

        // Act: wrap the native event in the mirror and read .Properties —
        // this is what a Serilog enricher or sink does.
        var mirrorEvent = new MMP.Herald.Serilog.Events.LogEvent(nativeEvent);
        _ = mirrorEvent.Properties; // trigger lazy projection

        // Assert: exactly one projection call — no double-project, no skip.
        LogEventValueProjector.ProjectionCallCount.Should().Be(1,
            "reading .Properties on the mirror event must trigger projection " +
            "exactly once; more means double-projection; zero means the " +
            "counter itself is broken (positive-control failure).");
    }

    /// <summary>
    /// Native path does NOT trigger projection: logging through the adapter
    /// with a null sink leaves <c>ProjectionCallCount</c> at zero, because
    /// the hot path never constructs or reads a mirror <see cref="LogEvent"/>.
    /// </summary>
    [Fact]
    public void Native_path_does_not_call_projector()
    {
        LogEventValueProjector.ResetCallCount();

        var adapter = new SerilogLoggerAdapter(_nullSinkVerbose.Logger);

        // Log several events — none should reach the projector.
        adapter.Information("msg {A}", "alpha");
        adapter.Warning("msg {B}", "bravo");
        adapter.Debug("msg {C}", "charlie");

        LogEventValueProjector.ProjectionCallCount.Should().Be(0,
            "the native path through SerilogLoggerAdapter must never call " +
            "LogEventValueProjector.Project — Guard 2 guarantees the hot path " +
            "is projection-free until a consumer reads .Properties.");
    }
}

/// <summary>
/// Disables parallelization for the Guard2 collection so the static
/// <c>LogEventValueProjector.ProjectionCallCount</c> is not corrupted by
/// concurrent test runs from other tests in this collection.
/// </summary>
[CollectionDefinition("Guard2", DisableParallelization = true)]
public sealed class Guard2Collection { }

#endif
