#nullable enable
#if NET9_0_OR_GREATER

using System;
using FluentAssertions;
using MMP.Herald.Levels;
using MMP.Herald.Quick;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Configuration;
using MMP.Herald.Serilog.Events;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog.Configuration;

/// <summary>
/// Task 5 — end-to-end <c>CreateLogger()</c> parity.
///
/// <para>
/// Covers four slices of the G-CORPUS.1 contract:
/// <list type="number">
///   <item>Shape parity — the canonical multi-option fluent chain and an
///         equivalent hand-built <see cref="QuickLogBuilder"/> produce identical
///         pipeline JSON (translator round-trip check).</item>
///   <item>Usability — <c>CreateLogger()</c> returns a live
///         <see cref="global::MMP.Herald.Serilog.ILogger"/> and calling
///         <c>Information</c> on it does not throw.</item>
///   <item>Stale-builder reuse (R-04 pre-mortem) — mutations made to a
///         <see cref="LoggerConfiguration"/> after a first <c>CreateLogger()</c>
///         call are visible in the second call's builder JSON.  This is
///         documented parity with Serilog's own behaviour, not a bug.</item>
///   <item>Return-type contract — <c>CreateLogger()</c> returns an instance that
///         implements <see cref="global::MMP.Herald.Serilog.ILogger"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// Enrich.WithProperty is wired (Task 4) and is included in the canonical chain.
/// A skip-marked placeholder records the gap for any further enricher variants
/// that are not yet implemented (ILogEventEnricher bridge).
/// </para>
/// </summary>
public sealed class CreateLoggerParityTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    // Produce the direct-path QuickLogBuilder equivalent of the canonical
    // multi-option chain used by Test 1. Separated so both the JSON-shape
    // test and any future snapshot tests can use the same reference.
    //
    // Chain being mirrored:
    //   .MinimumLevel.Debug()
    //   .MinimumLevel.Override("Microsoft", Warning)
    //   .WriteTo.Console()
    //   .WriteTo.File("app.ndjson", restrictedToMinimumLevel: Error)
    //   .Enrich.WithProperty("App", "Checkout")
    //
    // MinimumLevel.Debug()  → WithMinimumLevel("debug")
    // MinimumLevel.Override → auto-creates an Information-floor switch internally,
    //                         then calls WithFastDynamicLevel(switch, map).
    //                         The direct path replicates the same Information floor.
    // WriteTo.Console       → WithConsoleSink()
    // WriteTo.File          → WithFileSink("app.ndjson", minLevel: "error")
    // Enrich.WithProperty   → WithFastEnrichment(new LogProperty("App","Checkout"))
    private static QuickLogBuilder BuildCanonicalReference()
    {
        // Information-floor switch + Microsoft→warning override map.
        // The translator auto-creates new LogLevelSwitch(KnownLogLevels.Information)
        // on the first Override() call when no ControlledBy() has fired yet.
        var globalSwitch = new LogLevelSwitch(KnownLogLevels.Information);
        var overrides = new CategoryLevelSwitchMap();
        overrides.SetCategoryLevel("Microsoft", KnownLogLevels.Warning);

        return QuickLogBuilder.Create()
            .WithMinimumLevel("debug")
            .WithFastDynamicLevel(globalSwitch, overrides)
            .WithConsoleSink()
            .WithFileSink("app.ndjson", minLevel: "error")
            .WithFastEnrichment(new LogProperty("App", "Checkout"));
    }

    // ── Test 1 — canonical multi-option shape parity ──────────────────────────

    [Fact]
    public void Canonical_chain_produces_same_json_as_hand_built_reference()
    {
        // Arrange — Serilog-compat translator path
        var config = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.Console()
            .WriteTo.File("app.ndjson", LogEventLevel.Error)
            .Enrich.WithProperty("App", "Checkout");

        // Arrange — direct QuickLogBuilder reference (ground truth)
        var reference = BuildCanonicalReference();

        // Assert — JSON must be identical; any translator gap surfaces here
        TranslatorParityOracle.AssertSameShape(config.Builder, reference);
    }

    // P4 S2 is now wired. JSON parity with a native enricher is NOT achievable because
    // SerilogEnricherAdapter.ToJsonConfig() emits a non-standard adapter token — the
    // ILogEventEnricher cannot round-trip through JSON (documented gap, pinned by
    // CustomEnricherAdapterTests.ToJsonConfig_emits_bare_type_name_known_gap).
    //
    // This test verifies the builder shape DOES differ from a native enricher registration,
    // confirming the gap is visible (not silently hidden) and pinning it for future work.
    [Fact]
    public void Enrich_With_ILogEventEnricher_adapter_json_differs_from_native_enricher_known_gap()
    {
        // Arrange: Serilog path
        var serilogConfig = new LoggerConfiguration();
        serilogConfig.Enrich.With(new StubSerilogEnricher());
        serilogConfig.WriteTo.Null();

        // Arrange: native Herald path
        var nativeConfig = new LoggerConfiguration();
        nativeConfig.Builder.WithEnrichers(new global::MMP.Herald.Enrichers.MachineNameLogEnricher());
        nativeConfig.WriteTo.Null();

        // The JSON shapes must differ because the adapter emits a non-standard token.
        // If they were ever equal it would mean the gap was closed — update this test then.
        var serilogJson = serilogConfig.Builder.ExportConfigJson();
        var nativeJson = nativeConfig.Builder.ExportConfigJson();
        serilogJson.Should().NotBe(nativeJson,
            "SerilogEnricherAdapter emits a non-standard JSON token (known gap); " +
            "if these are ever equal the gap has been closed and this test should be updated");
    }

    private sealed class StubSerilogEnricher
        : MMP.Herald.Serilog.Core.ILogEventEnricher
    {
        public void Enrich(
            MMP.Herald.Serilog.Events.LogEvent logEvent,
            MMP.Herald.Serilog.Events.ILogEventPropertyFactory propertyFactory)
        {
            // No-op: shape test only.
        }
    }

    // ── Test 2 — CreateLogger() returns a usable ILogger ─────────────────────

    [Fact]
    public void CreateLogger_returns_non_null_ILogger_and_Information_call_does_not_throw()
    {
        // Arrange — minimal pipeline with a null sink so Build() succeeds
        var config = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Null();

        // Act
        var logger = config.CreateLogger();

        // Assert — non-null
        logger.Should().NotBeNull("CreateLogger must return a live ILogger");

        // Assert — calling Information must not throw (verifies the pipeline
        // round-trip: builder → Build() → SerilogLoggerAdapter → Herald StructuredLogger)
        var act = () => logger.Information("test {X}", 42);
        act.Should().NotThrow("an Information call on a null-sink pipeline must not throw");
    }

    [Fact]
    public void CreateLogger_with_console_and_file_does_not_throw()
    {
        // Smoke-test the full canonical chain end-to-end via CreateLogger.
        // MinimumLevel + Override + two WriteTo + Enrich must compose without
        // throwing during Build() or adapter construction.
        var config = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.Console()
            .WriteTo.File("app.ndjson", LogEventLevel.Error)
            .Enrich.WithProperty("App", "Checkout");

        var act = () => config.CreateLogger();

        act.Should().NotThrow("the full canonical chain must build without throwing");
    }

    // ── Test 3 — stale-builder reuse (R-04 pre-mortem) ───────────────────────

    /// <summary>
    /// Documents the stale-builder reuse behaviour that matches Serilog's own contract:
    /// a second <c>CreateLogger()</c> call reflects mutations that were applied to the
    /// <see cref="LoggerConfiguration"/> after the first call.  This is intentional,
    /// not a bug — the underlying <see cref="QuickLogBuilder"/> is mutable and each
    /// <c>CreateLogger()</c> call reads the builder's current state.
    ///
    /// <para>
    /// R-04 implication: callers who want an immutable snapshot must not share a
    /// <c>LoggerConfiguration</c> instance across creation sites.  Document this
    /// at the creation API boundary; do not silently discard mutations.
    /// </para>
    /// </summary>
    [Fact]
    public void CreateLogger_second_call_reflects_mutations_made_after_first_call()
    {
        // Arrange — build a first logger from a console-only pipeline
        var config = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console();

        var logger1 = config.CreateLogger(); // builds from Console-only state

        // Mutate the config after the first build — add a File sink
        config.WriteTo.File("app.ndjson");

        // Act — second CreateLogger reads the updated builder
        var logger2 = config.CreateLogger();

        // Assert — builder JSON now contains the file sink that was added
        // between the two CreateLogger() calls (documented R-04 behaviour)
        config.Builder.ExportConfigJson()
            .Should().Contain("ndjson",
                "mutations made after the first CreateLogger() are reflected in the builder — " +
                "this is documented parity with Serilog's own stale-builder contract (R-04)");

        // Both logger references must be non-null (the build step itself did not fail)
        logger1.Should().NotBeNull();
        logger2.Should().NotBeNull();
    }

    [Fact]
    public void First_and_second_CreateLogger_builder_json_differ_after_sink_mutation()
    {
        // Capture the JSON snapshot before mutation and compare to the post-mutation
        // snapshot to confirm the builder is indeed mutable across calls.
        var config = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console();

        // Snapshot before mutation
        var jsonBefore = config.Builder.ExportConfigJson();

        config.CreateLogger();                       // first build
        config.WriteTo.File("app.ndjson");           // mutate
        config.CreateLogger();                       // second build

        // Snapshot after mutation
        var jsonAfter = config.Builder.ExportConfigJson();

        jsonAfter.Should().NotBe(jsonBefore,
            "the builder JSON must change after WriteTo.File is added post-first-build (R-04)");
        jsonAfter.Should().Contain("ndjson",
            "the post-mutation JSON must contain the newly added file sink path");
    }

    // ── Test 4 — return-type contract ─────────────────────────────────────────

    [Fact]
    public void CreateLogger_returns_instance_assignable_to_Herald_Serilog_ILogger()
    {
        // Arrange — any valid pipeline (null sink satisfies the build validator)
        var config = new LoggerConfiguration()
            .WriteTo.Null();

        // Act
        var logger = config.CreateLogger();

        // Assert — the return type must implement the Herald Serilog ILogger contract.
        // BeAssignableTo covers both direct implementation and interface inheritance.
        logger.Should().BeAssignableTo<global::MMP.Herald.Serilog.ILogger>(
            "CreateLogger must return a type that implements MMP.Herald.Serilog.ILogger");
    }

    [Fact]
    public void CreateLogger_returns_SerilogLoggerAdapter()
    {
        // The concrete type must be SerilogLoggerAdapter — the only bridge between
        // Herald's StructuredLogger and the Serilog ILogger surface in the compat layer.
        var config = new LoggerConfiguration()
            .WriteTo.Null();

        var logger = config.CreateLogger();

        logger.Should().BeOfType<SerilogLoggerAdapter>(
            "CreateLogger must return a SerilogLoggerAdapter (the P1 bridge type)");
    }
}

#endif
