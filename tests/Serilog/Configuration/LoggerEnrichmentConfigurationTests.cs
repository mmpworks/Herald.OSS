#nullable enable
#if NET9_0_OR_GREATER

using FluentAssertions;
using MMP.Herald.OSS.Tests.Serilog.Configuration;
using MMP.Herald.Quick;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Configuration;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog.Configuration;

/// <summary>
/// P2 Task 4 — LoggerEnrichmentConfiguration translator correctness.
///
/// <para>
/// Each test builds the same pipeline configuration two ways:
///   1. Via the Serilog-compat translator (LoggerConfiguration → Enrich.*).
///   2. Via direct QuickLogBuilder calls (the ground truth).
/// The TranslatorParityOracle asserts they produce identical JSON.
/// </para>
///
/// <para>
/// Gaps pinned by skip-marked tests:
///   - <c>With(ILogEventEnricher)</c>: awaits P4 S2 enricher bridge.
///   - <c>destructureObjects: true</c>: silently ignored (no CaptureMode on fast path).
/// </para>
/// </summary>
public sealed class LoggerEnrichmentConfigurationTests
{
    // ExportConfigJson() requires at least one sink.
    // WithNullSink() satisfies the requirement without affecting enrichment JSON.
    private static LoggerConfiguration WithNullSink(LoggerConfiguration c)
    {
        c.WriteTo.Null();
        return c;
    }

    private static QuickLogBuilder WithNullSink(QuickLogBuilder b)
    {
        b.WithNullSink();
        return b;
    }

    // ── WithProperty ──────────────────────────────────────────────────────────

    [Fact]
    public void WithProperty_maps_to_WithFastEnrichment_string_value()
    {
        var config = new LoggerConfiguration();
        config.Enrich.WithProperty("App", "Checkout");
        WithNullSink(config);

        var reference = WithNullSink(QuickLogBuilder.Create()
            .WithFastEnrichment(new global::MMP.Herald.Templating.LogProperty("App", "Checkout")));

        TranslatorParityOracle.AssertSameShape(config.Builder, reference);
    }

    [Fact]
    public void WithProperty_works_with_numeric_value()
    {
        var config = new LoggerConfiguration();
        config.Enrich.WithProperty("RequestId", 42);
        WithNullSink(config);

        var reference = WithNullSink(QuickLogBuilder.Create()
            .WithFastEnrichment(new global::MMP.Herald.Templating.LogProperty("RequestId", 42)));

        TranslatorParityOracle.AssertSameShape(config.Builder, reference);
    }

    [Fact]
    public void WithProperty_chained_calls_accumulate_both_properties()
    {
        var config = new LoggerConfiguration();
        config.Enrich
            .WithProperty("Env", "prod")
            .Enrich.WithProperty("Version", "2.0");
        WithNullSink(config);

        var reference = WithNullSink(QuickLogBuilder.Create()
            .WithFastEnrichment(new global::MMP.Herald.Templating.LogProperty("Env", "prod"))
            .WithFastEnrichment(new global::MMP.Herald.Templating.LogProperty("Version", "2.0")));

        TranslatorParityOracle.AssertSameShape(config.Builder, reference);
    }

    [Fact]
    public void WithProperty_returns_root_LoggerConfiguration_for_fluent_chaining()
    {
        var config = new LoggerConfiguration();
        var returned = config.Enrich.WithProperty("X", "y");
        returned.Should().BeSameAs(config, "WithProperty must return the root LoggerConfiguration");
    }

    // ── destructureObjects (known gap, silently ignored) ─────────────────────

    [Fact]
    public void WithProperty_with_destructureObjects_true_does_not_throw()
    {
        // Known gap: destructureObjects is ignored in the fast-enrichment path
        // (no CaptureMode override). Matches the ForContext(destructureObjects:true)
        // gap from P1. The call must not throw — API compatibility first.
        var config = new LoggerConfiguration();
        var act = () => config.Enrich.WithProperty("Order", new { Sku = "A1" }, destructureObjects: true);
        act.Should().NotThrow("destructureObjects is silently ignored, matching ForContext P1 behavior");
    }

    // ── FromLogContext ────────────────────────────────────────────────────────

    [Fact]
    public void FromLogContext_is_a_no_op_on_the_builder()
    {
        // P1 already wires the async-local scope provider. FromLogContext() must
        // not change the JSON output — the two configs must be shape-identical.
        var configWith = new LoggerConfiguration();
        configWith.Enrich.FromLogContext();
        WithNullSink(configWith);

        var configWithout = new LoggerConfiguration();
        WithNullSink(configWithout);

        TranslatorParityOracle.AssertSameShape(configWith.Builder, configWithout.Builder);
    }

    [Fact]
    public void FromLogContext_returns_root_LoggerConfiguration_for_fluent_chaining()
    {
        var config = new LoggerConfiguration();
        var returned = config.Enrich.FromLogContext();
        returned.Should().BeSameAs(config, "FromLogContext must return the root LoggerConfiguration");
    }

    // ── With(ILogEventEnricher) — P4 S2 bridge implemented ──────────────────

    [Fact]
    public void With_ILogEventEnricher_routes_to_WithEnrichers()
    {
        // Arrange
        var config = new LoggerConfiguration();
        config.Enrich.With(new StaticTagEnricher("test"));
        WithNullSink(config);

        // Assert: the builder has one enricher registered (the adapter wrapper).
        // We verify via the builder's enricher count rather than JSON shape because
        // SerilogEnricherAdapter emits a non-standard config token (known gap).
        config.Builder.Enrichers.Count.Should().Be(1,
            "With(ILogEventEnricher) must register exactly one ILogEnricher adapter");
    }

    // ── Test double used by With(ILogEventEnricher) test ────────────────────

    private sealed class StaticTagEnricher(string tag) : MMP.Herald.Serilog.Core.ILogEventEnricher
    {
        public void Enrich(
            MMP.Herald.Serilog.Events.LogEvent logEvent,
            MMP.Herald.Serilog.Events.ILogEventPropertyFactory propertyFactory)
        {
            var prop = propertyFactory.CreateProperty("Tag", tag);
            logEvent.AddOrUpdateProperty(prop);
        }
    }
}

#endif
