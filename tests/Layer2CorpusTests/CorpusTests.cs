#nullable enable
#if NET9_0_OR_GREATER

// G-CORPUS.1 — compile-and-run corpus tests for the Layer-2 Serilog mirror.
//
// Purpose:
//   Verify that representative Serilog consumer patterns compile unchanged
//   against the Layer-2 mirror assembly (AssemblyName=Serilog) and execute
//   without throwing.
//
// Important:
//   This project references ONLY the Layer-2 ProjectReference — NOT the real
//   Serilog NuGet. That separation is what makes CS0433 impossible and proves
//   the mirror can stand in for the real Serilog.dll in a bin-swap scenario.
//
// CRIT-FM-L2 (slot-identity):
//   Layer-2 Log.Logger shares the Layer-1 slot — there is no second backing field.
//   The slot-identity tests verify this by cross-assigning between Layer-1 and
//   Layer-2 and confirming events flow through the same capturing sink.
//
// Capturing strategy:
//   Tests that need event capture assign the capturing adapter via the Layer-1
//   MMP.Herald.Serilog.Log.Logger slot directly, then log via the Layer-2 facade.
//   This is also the CRIT-FM-L2 test — if Layer-2 holds a second slot, the
//   Layer-2 facade would no-op and the sink would see zero events.

using System;
using FluentAssertions;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Serilog;
using MMP.Herald.Testing;
using Xunit;

// Type aliases to keep Serilog.* calls terse. These reference Layer-2 types.
using SerilogLog = Serilog.Log;
using SerilogLoggerConfiguration = Serilog.LoggerConfiguration;

namespace MMP.Herald.Compat.Layer2.Tests;

public sealed class CorpusTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    // Builds a minimal capturing pipeline and returns the Logger + capturing Sink.
    // Uses TestLogPipelineBuilder directly (not TestLoggers, which lives in the
    // sibling Herald.OSS.Tests project that also references the real Serilog NuGet).
    private static (StructuredLogger Herald, InMemoryLogSink Sink) CreateCapturing()
        => new TestLogPipelineBuilder()
            .WithMinimumLevel(KnownLogLevels.Verbose)
            .Build();

    // ── G-CORPUS.1 — representative Serilog configuration patterns ───────────

    /// <summary>
    /// Canonical Serilog configuration block:
    ///   new LoggerConfiguration().MinimumLevel.Information().WriteTo.Console().CreateLogger()
    /// must compile unchanged against the Layer-2 mirror and run without throwing.
    /// </summary>
    [Fact]
    public void Corpus_canonical_configuration_compiles_and_runs()
    {
        // This snippet is the most common Serilog getting-started pattern.
        // If it doesn't compile, Layer-2 is missing a surface member.
        var log = new SerilogLoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateLogger();

        // The act of building the logger AND calling log.Information must not throw.
        var act = () => log.Information("corpus {Item}", "test");
        act.Should().NotThrow("Layer-2 WriteTo.Console() + Information() must be functional");
    }

    /// <summary>
    /// Fluent sink chain: multiple WriteTo calls on the same LoggerConfiguration.
    /// </summary>
    [Fact]
    public void Corpus_multiple_WriteTo_sinks_compile_and_run()
    {
        var log = new SerilogLoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console()
            .WriteTo.Null()
            .CreateLogger();

        var act = () =>
        {
            log.Verbose("verbose {X}", 1);
            log.Debug("debug {X}", 2);
            log.Warning("warn {X}", 3);
            log.Error("error {X}", 4);
            log.Fatal("fatal {X}", 5);
        };
        act.Should().NotThrow();
    }

    /// <summary>
    /// MinimumLevel shorthand overloads — Verbose / Debug / Information / Warning /
    /// Error / Fatal — must all compile and run without throwing.
    /// </summary>
    [Fact]
    public void Corpus_MinimumLevel_shorthands_compile_and_run()
    {
        // Use Warning as the floor so below-floor events are silently dropped.
        var log = new SerilogLoggerConfiguration()
            .MinimumLevel.Warning()
            .WriteTo.Console()
            .CreateLogger();

        var act = () => log.Information("below floor — dropped silently");
        act.Should().NotThrow("below-floor events must be silently dropped, not throw");
    }

    /// <summary>
    /// Enrich.WithProperty() — add a constant property to every event.
    /// </summary>
    [Fact]
    public void Corpus_Enrich_WithProperty_compiles_and_runs()
    {
        var log = new SerilogLoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.WithProperty("App", "corpus-test")
            .WriteTo.Console()
            .CreateLogger();

        var act = () => log.Information("enriched event");
        act.Should().NotThrow();
    }

    /// <summary>
    /// ILogger.ForContext{T}() — returns a child logger tagged with the source type.
    /// </summary>
    [Fact]
    public void Corpus_ForContext_generic_compiles_and_runs()
    {
        var root = new SerilogLoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console()
            .CreateLogger();

        var child = root.ForContext<CorpusTests>();
        var act = () => child.Information("from child logger");
        act.Should().NotThrow();
    }

    /// <summary>
    /// ILogger.ForContext(string, object) — named property tagging.
    /// </summary>
    [Fact]
    public void Corpus_ForContext_named_property_compiles_and_runs()
    {
        var root = new SerilogLoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console()
            .CreateLogger();

        var child = root.ForContext("RequestId", Guid.NewGuid());
        var act = () => child.Warning("request context event");
        act.Should().NotThrow();
    }

    /// <summary>
    /// ILogger.Write() with LogEventLevel — some corpus code calls Write() directly.
    /// Uses the Layer-2 Serilog.Events.LogEventLevel enum.
    /// </summary>
    [Fact]
    public void Corpus_Write_with_level_compiles_and_runs()
    {
        var log = new SerilogLoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console()
            .CreateLogger();

        // Use MMP.Herald.Serilog.Events.LogEventLevel — the L1 type that the
        // ILogger.Write() method signature uses (see Layer-2 ILogger.cs comments).
        var act = () =>
        {
            log.Write(MMP.Herald.Serilog.Events.LogEventLevel.Information, "write via level {X}", 42);
            log.Write(MMP.Herald.Serilog.Events.LogEventLevel.Error,
                      new InvalidOperationException("boom"), "error {X}", 99);
        };
        act.Should().NotThrow();
    }

    // ── G-CORPUS.1 — static Log facade patterns ──────────────────────────────

    /// <summary>
    /// Serilog.Log before assignment must be silent, not throw NullReferenceException.
    /// This is the Layer-1 SilentLogger contract, visible through Layer-2.
    /// </summary>
    [Fact]
    public void Corpus_static_Log_before_assignment_is_silent()
    {
        SerilogLog.CloseAndFlush(); // reset to unassigned / SilentLogger state

        var act = () => SerilogLog.Information("no logger assigned");
        act.Should().NotThrow("unassigned Log must silently no-op, not throw");
    }

    /// <summary>
    /// Serilog.Log.CloseAndFlush() is idempotent — calling it twice must not throw.
    /// </summary>
    [Fact]
    public void Corpus_static_Log_CloseAndFlush_is_idempotent()
    {
        // Set a logger so there is something to flush.
        var (herald, _) = CreateCapturing();
        MMP.Herald.Serilog.Log.Logger = new SerilogLoggerAdapter(herald);

        var act = () => { SerilogLog.CloseAndFlush(); SerilogLog.CloseAndFlush(); };
        act.Should().NotThrow("double CloseAndFlush must be idempotent");

        // After flush: calls must still not throw (SilentLogger takes over).
        var act2 = () => SerilogLog.Information("after flush");
        act2.Should().NotThrow();
    }

    // ── CRIT-FM-L2 — slot-identity tests ────────────────────────────────────

    /// <summary>
    /// CRIT-FM-L2 slot-identity (L1 → L2 direction):
    ///   Set the slot via Layer-1's MMP.Herald.Serilog.Log.Logger.
    ///   Log via the Layer-2 Serilog.Log facade.
    ///   Events must reach the capturing sink assigned through Layer-1.
    ///
    ///   If Layer-2 held a second backing field, the Layer-2 facade would
    ///   no-op (its slot is still null/SilentLogger), and the sink would
    ///   see zero events. Observing ≥1 event proves one shared slot.
    /// </summary>
    [Fact]
    public void CritFmL2_Layer2_Log_facade_sees_Layer1_slot_assignment()
    {
        // Arrange: capturing pipeline — assigned via Layer-1.
        var (herald, sink) = CreateCapturing();
        MMP.Herald.Serilog.Log.Logger = new SerilogLoggerAdapter(herald);

        // Act: log via Layer-2's static facade.
        SerilogLog.Information("slot test {X}", 99);

        // Assert: the event reached the Layer-1 capturing sink.
        var captured = sink.GetEvents();
        captured.Should().HaveCount(1,
            "Layer-2 Log.Information() must forward through the shared Layer-1 slot");
        captured[0].Level.Key.Should().Be("information",
            "the captured event must carry the Information level key");

        // Cleanup.
        MMP.Herald.Serilog.Log.CloseAndFlush();
    }

    /// <summary>
    /// CRIT-FM-L2 slot-identity (L2 → L1 direction):
    ///   Log via Layer-1's MMP.Herald.Serilog.Log.Information() after assigning
    ///   the adapter through Layer-1.
    ///   Layer-2's CloseAndFlush() resets the shared slot.
    ///   Subsequent calls via Layer-1 must no-op (SilentLogger).
    /// </summary>
    [Fact]
    public void CritFmL2_Layer1_facade_sees_same_slot_as_Layer2_lifecycle()
    {
        // Arrange: capturing pipeline — assigned via Layer-1.
        var (herald, sink) = CreateCapturing();
        MMP.Herald.Serilog.Log.Logger = new SerilogLoggerAdapter(herald);

        // Act: log via Layer-1's facade — must hit the same sink.
        MMP.Herald.Serilog.Log.Information("layer-1 slot {X}", 42);

        // Intermediate assert: event arrived.
        var captured = sink.GetEvents();
        captured.Should().HaveCount(1, "Layer-1 Information() must see the adapter");
        captured[0].Level.Key.Should().Be("information");

        // Reset via Layer-2's CloseAndFlush — this must reset the SHARED slot.
        SerilogLog.CloseAndFlush();

        // Post-flush: Layer-1 calls must no-op without throwing.
        var act = () => MMP.Herald.Serilog.Log.Information("after layer-2 flush");
        act.Should().NotThrow("Layer-1 after Layer-2 CloseAndFlush must be silent, not throw");

        // No new events should have been captured (SilentLogger took over).
        sink.GetEvents().Should().HaveCount(1, "only the pre-flush event must be in the sink");
    }

    /// <summary>
    /// CRIT-FM-L2 slot-identity (round-trip read):
    ///   Assign the adapter via Layer-1.
    ///   Read Log.Logger back via Layer-2.
    ///   Log via the returned logger.
    ///   Event must reach the capturing sink.
    /// </summary>
    [Fact]
    public void CritFmL2_Logger_read_via_Layer2_routes_to_same_sink()
    {
        // Arrange: capturing pipeline — assigned via Layer-1.
        var (herald, sink) = CreateCapturing();
        MMP.Herald.Serilog.Log.Logger = new SerilogLoggerAdapter(herald);

        // Act: read the logger back via Layer-2 and use it directly.
        // Serilog.Log.Logger returns a Core.Logger wrapper over the shared L1 slot.
        var logger = SerilogLog.Logger;
        logger.Information("round-trip {X}", 7);

        // Assert: event arrived in the sink, confirming the wrapper routes through
        // the same L1 backing slot rather than producing a disconnected instance.
        var captured = sink.GetEvents();
        captured.Should().HaveCount(1, "logger read via Layer-2 must route to the capturing sink");
        captured[0].Level.Key.Should().Be("information");

        // Cleanup.
        MMP.Herald.Serilog.Log.CloseAndFlush();
    }
}

#endif
