#nullable enable

using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Configuration;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Events;
using MMP.Herald.Serilog.Formatting;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog.Seams;

/// <summary>
/// G-CORPUS.4 — end-to-end compile-and-run corpus.
///
/// <para>
/// Proves that representative user-authored extensions (custom sink, custom enricher,
/// custom formatter) compile against the MMP.Herald.Serilog shim and run correctly,
/// exercising the mirror path from user code all the way through to the native
/// Herald pipeline.
/// </para>
///
/// <para>
/// Three primary corpus entries:
/// <list type="number">
///   <item>Custom <see cref="ILogEventSink"/> — receives a mirrored LogEvent; template
///         property projected as StructureValue via {@} operator.</item>
///   <item>Custom <see cref="ILogEventEnricher"/> — adds a structured property via
///         <c>destructureObjects: true</c>; arrives as StructureValue, not ScalarValue.</item>
///   <item>Custom <see cref="ITextFormatter"/> — writes its own JSON shape; captures output
///         directly; level and rendered value appear in the produced string.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Guard-2 positive control (corpus variant).</b>
/// Echo's test inventory flagged that the Guard-2 zero-projection assertion needs a
/// sibling positive control via the sink-dispatch path: a sink that explicitly reads
/// <c>logEvent.Properties</c> inside <c>Emit()</c> should drive ProjectionCallCount to 1.
/// The existing Guard-2 suite covers this via the direct-wrap path; this corpus entry
/// adds the sink-dispatch variant so both trigger paths are covered.
/// Because it shares the static <c>LogEventValueProjector.ProjectionCallCount</c> counter,
/// this test is placed in the <c>Guard2</c> xUnit collection (DisableParallelization = true).
/// </para>
///
/// <para>Gated to net9.0+: MMP.Herald.Serilog does not target net8.</para>
/// </summary>
[Collection("Guard2")]
public sealed class CorpusTests
{
    // ── Corpus 1: custom ILogEventSink ────────────────────────────────────────

    /// <summary>
    /// A user-authored ILogEventSink compiled from source (not a pre-compiled NuGet
    /// package) receives a mirrored LogEvent. The destructured property captured via
    /// the {@Order} operator is projected as a StructureValue, not a ScalarValue.
    /// </summary>
    [Fact]
    public void User_ILogEventSink_compiles_and_runs_against_shim()
    {
        // Arrange
        var received = new List<LogEvent>();
        var log = new LoggerConfiguration()
            .WriteTo.Sink(new AuditSinkSample(received))
            .CreateLogger();

        // Act
        log.Information("order {@Order}", new { Id = "ord-99", Amount = 42.5m });

        // Assert
        var evt = Assert.Single(received);
        evt.Level.Should().Be(LogEventLevel.Information,
            "Information must map directly to Serilog's Information level");

        // Property via tree projection: {@Order} uses destructure mode, so the shim
        // must walk the anonymous object and return a StructureValue, not a scalar.
        evt.Properties.Should().ContainKey("Order",
            "the template hole {@Order} must appear as a key in the projected properties");
        evt.Properties["Order"].Should().BeOfType<StructureValue>(
            "{@Order} uses the destructure capture mode; projection must produce StructureValue, " +
            "not ScalarValue — if this fails the mirror is ignoring the capture-mode flag");
    }

    // ── Corpus 2: custom ILogEventEnricher with destructureObjects:true ──────

    /// <summary>
    /// A user-authored ILogEventEnricher uses ILogEventPropertyFactory to create a
    /// property with <c>destructureObjects: true</c>. The resulting property must
    /// arrive at the sink as a StructureValue (the S2 seam contract).
    /// </summary>
    [Fact]
    public void User_ILogEventEnricher_adds_structured_property_to_event()
    {
        // Arrange
        var received = new List<LogEvent>();
        var log = new LoggerConfiguration()
            .Enrich.With(new RequestContextEnricher())
            .WriteTo.Sink(new AuditSinkSample(received))
            .CreateLogger();

        // Act
        log.Information("request");

        // Assert
        var evt = Assert.Single(received);
        evt.Properties.Should().ContainKey("RequestContext",
            "the enricher must add RequestContext via ILogEventPropertyFactory");

        // The enricher used destructureObjects:true — must be StructureValue, not scalar.
        // This is the S2 load-bearing contract: the shim must route the flag to
        // LogPropertyCaptureMode.Destructure so the projector walks the object graph.
        evt.Properties["RequestContext"].Should().BeOfType<StructureValue>(
            "destructureObjects:true must produce StructureValue via the projection walk, " +
            "not ScalarValue — if this fails the shim is silently ignoring the flag");
    }

    // ── Corpus 3: custom ITextFormatter ──────────────────────────────────────

    /// <summary>
    /// A user-authored ITextFormatter writes its own JSON shape. The formatter receives
    /// a mirrored LogEvent, produces output, and the level and rendered value appear in
    /// the captured string.
    /// </summary>
    [Fact]
    public void User_ITextFormatter_compiles_and_produces_custom_output()
    {
        // Arrange
        var outputs = new List<string>();
        var log = new LoggerConfiguration()
            .WriteTo.Console(new CustomJsonLineFormatter(outputs))
            .CreateLogger();

        // Act
        log.Warning("payment {Amount} failed", 99.99m);

        // Assert: the formatter was called and produced its custom JSON shape.
        outputs.Should().HaveCount(1,
            "the formatter must be invoked exactly once for a single logged event");
        outputs[0].Should().Contain("\"level\":\"warning\"",
            "the formatter's own JSON shape must include the lowercased level key");
        outputs[0].Should().Contain("99.99",
            "the rendered message must contain the decimal value from the template hole");
    }

    // ── Guard-2 positive control (sink-dispatch variant) ─────────────────────

    /// <summary>
    /// Positive control via the sink-dispatch path: a sink that explicitly reads
    /// <c>logEvent.Properties</c> inside <c>Emit()</c> drives ProjectionCallCount to 1.
    ///
    /// <para>
    /// The existing Guard-2 suite proves the counter works via the direct-wrap path
    /// (constructing a mirror LogEvent manually). This variant closes the gap by proving
    /// the same counter increments when projection is triggered through the normal
    /// WriteTo.Sink dispatch — the path a real custom sink exercises at runtime.
    /// </para>
    ///
    /// <para>
    /// Without this control, a broken counter (always zero) would still pass the
    /// zero-count native-path assertion vacuously via the direct-wrap path, leaving
    /// the sink-dispatch path unverified.
    /// </para>
    /// </summary>
    [Fact]
    public void Custom_sink_reading_Properties_via_Emit_triggers_projection_exactly_once()
    {
        // Arrange: reset the static counter so this test starts clean.
        MMP.Herald.Serilog.Events.LogEventValueProjector.ResetCallCount();

        var received = new List<LogEvent>();
        var propertySink = new PropertyReadingSink(received);
        var log = new LoggerConfiguration()
            .WriteTo.Sink(propertySink)
            .CreateLogger();

        // Act: log one event; the sink will read .Properties inside Emit().
        log.Information("x {V}", 1);

        // Assert: exactly one projection call — the sink-dispatch path fires it once.
        // Zero means the counter itself is broken (or the sink never read .Properties).
        // More than one means double-projection via the dispatch path.
        MMP.Herald.Serilog.Events.LogEventValueProjector.ProjectionCallCount.Should().Be(1,
            "reading .Properties inside Emit() must trigger projection exactly once; " +
            "zero = counter broken or .Properties never called; " +
            "more than one = double-projection bug in the sink dispatch path");
    }

    // ── Test doubles ─────────────────────────────────────────────────────────

    /// <summary>
    /// A minimal user sink that records each emitted LogEvent.
    /// Compiled from source — demonstrates the custom-from-source contract.
    /// </summary>
    private sealed class AuditSinkSample(List<LogEvent> log) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => log.Add(logEvent);
    }

    /// <summary>
    /// Enricher that attaches a structured RequestContext property using
    /// <c>destructureObjects: true</c> via the ILogEventPropertyFactory shim.
    /// </summary>
    private sealed class RequestContextEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var ctx = new { CorrelationId = "x-99", UserId = "alice" };
            logEvent.AddOrUpdateProperty(
                propertyFactory.CreateProperty("RequestContext", ctx, destructureObjects: true));
        }
    }

    /// <summary>
    /// Formatter that produces a minimal JSON line: <c>{"level":"...","msg":"..."}</c>.
    /// Captures each produced string into <paramref name="outputs"/> for assertion.
    /// </summary>
    private sealed class CustomJsonLineFormatter(List<string> outputs) : ITextFormatter
    {
        public void Format(LogEvent logEvent, TextWriter output)
        {
            // Cognitive complexity note: straightforward string build.
            // logEvent.RenderMessage() returns the Herald pre-rendered message string,
            // which substitutes template holes with their formatted values.
            var json = $"{{\"level\":\"{logEvent.Level.ToString().ToLower()}\",\"msg\":\"{logEvent.RenderMessage()}\"}}";
            output.Write(json);
            outputs.Add(json);
        }
    }

    /// <summary>
    /// Sink that explicitly reads <see cref="LogEvent.Properties"/> inside
    /// <see cref="Emit"/> to trigger lazy projection — used by the Guard-2
    /// positive-control (sink-dispatch variant).
    /// </summary>
    private sealed class PropertyReadingSink(List<LogEvent> received) : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            received.Add(logEvent);
            _ = logEvent.Properties; // trigger lazy projection
        }
    }
}

