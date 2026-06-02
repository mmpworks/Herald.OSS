#nullable enable
#if NET9_0_OR_GREATER

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Configuration;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Events;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog.Seams;

/// <summary>
/// S2 seam: Enrich.With(ILogEventEnricher) adapter + ILogEventPropertyFactory shim.
///
/// <para>
/// Verifies that user-authored Serilog enrichers receive a Serilog-shaped LogEvent
/// view + ILogEventPropertyFactory and that properties they add appear on the
/// finalised event delivered to sinks.
/// </para>
///
/// <para>
/// Key contracts under test:
/// <list type="bullet">
///   <item>Scalar properties added via factory appear in sink-received events.</item>
///   <item><c>destructureObjects:true</c> routes through StructureValue, not ScalarValue.</item>
///   <item>Multiple enrichers chain correctly (all properties appear).</item>
///   <item><c>ToJsonConfig</c> round-trip emits a bare type-name token (documented gap).</item>
/// </list>
/// </para>
/// </summary>
public sealed class CustomEnricherAdapterTests
{
    // ── Primary scalar enrichment ─────────────────────────────────────────

    [Fact]
    public void Enricher_scalar_property_appears_on_event()
    {
        // Arrange
        var received = new List<LogEvent>();
        var recording = new RecordingLogEventSink(received);

        var log = new LoggerConfiguration()
            .Enrich.With(new TenantTaggingEnricher("acme"))
            .WriteTo.Sink(recording)
            .CreateLogger();

        // Act
        log.Information("request");

        // Assert
        var e = Assert.Single(received);
        e.Properties.Should().ContainKey("Tenant",
            "the enricher added Tenant via ILogEventPropertyFactory");
        var tenantValue = e.Properties["Tenant"];
        tenantValue.Should().BeOfType<ScalarValue>();
        ((ScalarValue)tenantValue).Value.Should().Be("acme");
    }

    // ── destructureObjects:true must produce StructureValue ──────────────

    [Fact]
    public void Enricher_destructure_true_routes_to_StructureValue_not_scalar()
    {
        // Arrange
        var received = new List<LogEvent>();
        var recording = new RecordingLogEventSink(received);

        var log = new LoggerConfiguration()
            .Enrich.With(new OrderTaggingEnricher())
            .WriteTo.Sink(recording)
            .CreateLogger();

        // Act
        log.Information("checkout");

        // Assert: destructureObjects:true must produce StructureValue, not ScalarValue.
        // This is the load-bearing S2 contract: the shim must route the flag to
        // LogPropertyCaptureMode.Destructure so the projector walks the object.
        var e = Assert.Single(received);
        e.Properties.Should().ContainKey("Order",
            "the enricher added Order with destructureObjects:true");
        var orderProp = e.Properties["Order"];
        orderProp.Should().BeOfType<StructureValue>(
            "destructureObjects:true must produce a StructureValue via the projection walk, " +
            "not a ScalarValue — if this fails the shim is silently ignoring the flag");
    }

    // ── Multiple chained enrichers ────────────────────────────────────────

    [Fact]
    public void Multiple_enrichers_all_add_their_properties()
    {
        // Arrange
        var received = new List<LogEvent>();
        var recording = new RecordingLogEventSink(received);

        var log = new LoggerConfiguration()
            .Enrich.With(new TenantTaggingEnricher("corp"))
            .Enrich.With(new CorrelationTaggingEnricher("abc-123"))
            .WriteTo.Sink(recording)
            .CreateLogger();

        // Act
        log.Warning("multi-enricher test");

        // Assert: both properties must appear.
        var e = Assert.Single(received);
        e.Properties.Should().ContainKey("Tenant",
            "first enricher must add Tenant");
        e.Properties.Should().ContainKey("CorrelationId",
            "second enricher must add CorrelationId");
    }

    // ── Enricher plus WriteTo.Sink combo ──────────────────────────────────

    [Fact]
    public void Enricher_and_sink_compose_cleanly()
    {
        // Verify that Enrich.With + WriteTo.Sink is a valid composition.
        var received = new List<LogEvent>();

        var log = new LoggerConfiguration()
            .Enrich.With(new TenantTaggingEnricher("x"))
            .WriteTo.Sink(new RecordingLogEventSink(received))
            .CreateLogger();

        log.Information("compose test");

        received.Should().HaveCount(1);
        received[0].Properties.Should().ContainKey("Tenant");
    }

    // ── Enricher that calls AddOrUpdateProperty directly (bypasses factory) ─

    [Fact]
    public void Enricher_AddOrUpdateProperty_direct_call_also_reaches_sink()
    {
        // Exercises the enrichment-time LogEvent.AddOrUpdateProperty path
        // that syncs back to the native context via UpsertProperty.
        var received = new List<LogEvent>();

        var log = new LoggerConfiguration()
            .Enrich.With(new DirectMutationEnricher("direct-value"))
            .WriteTo.Sink(new RecordingLogEventSink(received))
            .CreateLogger();

        log.Information("direct mutation test");

        var e = Assert.Single(received);
        e.Properties.Should().ContainKey("DirectProp",
            "properties added via logEvent.AddOrUpdateProperty must reach the sink");
    }

    // ── ToJsonConfig round-trip: documented gap ───────────────────────────

    [Fact]
    public void ToJsonConfig_emits_bare_type_name_known_gap()
    {
        // Known gap: the SerilogEnricherAdapter cannot round-trip the user enricher
        // through JSON because ILogEventEnricher has no Herald JSON-factory registration.
        // The config token is emitted as a bare type-name string so the gap is visible
        // rather than silently omitted. This test pins the current behaviour so any
        // future change is deliberate.
        var config = new LoggerConfiguration();
        config.Enrich.With(new TenantTaggingEnricher("test"));

        var adapter = config.Builder.Enrichers.Get<global::MMP.Herald.Enrichers.ILogEnricher>();
        adapter.Should().NotBeNull("the adapter must be registered");

        var json = adapter!.ToJsonConfig();
        json.Kind.Should().Contain("SerilogEnricherAdapter",
            "the round-trip must name the adapter so the gap is visible, not silently empty");
        json.Kind.Should().Contain("TenantTaggingEnricher",
            "the wrapped enricher type name must appear so the gap is identifiable");
    }

    // ── Test doubles ─────────────────────────────────────────────────────

    private sealed class TenantTaggingEnricher(string tenant) : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var prop = propertyFactory.CreateProperty("Tenant", tenant);
            logEvent.AddOrUpdateProperty(prop);
        }
    }

    private sealed class OrderTaggingEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            // destructureObjects:true — must produce StructureValue (the S2 contract).
            var prop = propertyFactory.CreateProperty("Order", new { Sku = "A1", Qty = 2 }, destructureObjects: true);
            logEvent.AddOrUpdateProperty(prop);
        }
    }

    private sealed class CorrelationTaggingEnricher(string correlationId) : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var prop = propertyFactory.CreateProperty("CorrelationId", correlationId);
            logEvent.AddOrUpdateProperty(prop);
        }
    }

    /// <summary>
    /// Builds a LogEventProperty manually and calls AddOrUpdateProperty directly
    /// (bypasses the factory shim). This exercises the sync-back path in the
    /// enrichment-time LogEvent variant.
    /// </summary>
    private sealed class DirectMutationEnricher(string value) : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            // Deliberately does NOT use the factory — calls AddOrUpdateProperty directly.
            var prop = new LogEventProperty("DirectProp", new ScalarValue(value));
            logEvent.AddOrUpdateProperty(prop);
        }
    }

    /// <summary>Records every emitted LogEvent into the supplied list.</summary>
    private sealed class RecordingLogEventSink(List<LogEvent> received) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => received.Add(logEvent);
    }
}

#endif
