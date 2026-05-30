#nullable enable

using System;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Enrichers;
using MMP.Herald.Templating;

namespace MMP.Herald.Serilog.Configuration;

/// <summary>
/// Fluent <c>Enrich.*</c> translator: maps each Serilog enrichment call to the
/// equivalent <see cref="MMP.Herald.Quick.QuickLogBuilder"/> enrichment method.
///
/// <para>
/// DRY contract: every method here is a one-line forwarder. No if/loop/format
/// logic belongs in this file — that work lives in QuickLogBuilder.
/// </para>
///
/// <para>
/// Known gap — <c>destructureObjects</c>:
/// Serilog's <c>WithProperty(name, value, destructureObjects: true)</c> triggers
/// destructuring via <c>IDestructuringPolicy</c>. Herald's fast enrichment path
/// stores raw <c>object?</c> without a CaptureMode override, so the flag is
/// silently ignored here. This matches the <c>ForContext(destructureObjects:true)</c>
/// gap documented in P1. Pinned by test.
/// </para>
///
/// <para>
/// <c>With(ILogEventEnricher)</c>:
/// Wraps the user-authored Serilog enricher in a <see cref="SerilogEnricherAdapter"/>
/// and registers it via <c>QuickLogBuilder.WithEnrichers</c>. The adapter bridges the
/// Serilog enrichment contract (mutable LogEvent + ILogEventPropertyFactory) onto the
/// native <see cref="MMP.Herald.Events.LogEventEnrichmentContext"/> API.
/// </para>
/// </summary>
public sealed class LoggerEnrichmentConfiguration
{
    private readonly LoggerConfiguration _root;

    internal LoggerEnrichmentConfiguration(LoggerConfiguration root) => _root = root;

    /// <summary>
    /// Add a constant-value enrichment property to every log event.
    /// Maps to <c>QuickLogBuilder.WithFastEnrichment(new LogProperty(name, value))</c>.
    ///
    /// <para>
    /// <paramref name="destructureObjects"/>: Serilog uses this flag to apply
    /// <c>IDestructuringPolicy</c> rules at capture time. Herald's fast-enrichment
    /// path stores raw <c>object?</c> and defers formatting decisions to the sink.
    /// The flag is accepted for API compatibility and silently ignored — same
    /// behavior as <c>ForContext(propertyName, value, destructureObjects: true)</c>
    /// from P1.
    /// </para>
    /// </summary>
    public LoggerConfiguration WithProperty(string name, object? value, bool destructureObjects = false)
    {
        // destructureObjects is intentionally unused: fast-enrichment stores raw
        // object? without CaptureMode. Accepted for API shape compatibility only.
        _root.Builder.WithFastEnrichment(new LogProperty(name, value));
        return _root;
    }

    /// <summary>
    /// No-op. P1 already wires scope/PushProperty ambient capture via the
    /// async-local scope provider; no additional builder mutation is needed.
    /// Returns the root <see cref="LoggerConfiguration"/> unchanged for fluent chaining.
    /// </summary>
    public LoggerConfiguration FromLogContext()
    {
        // P1 wires the async-local scope provider on every build. No builder
        // mutation is required here — calling this is a compile-time compatibility
        // marker, not a runtime action.
        return _root;
    }

    /// <summary>
    /// Register a user-authored Serilog <see cref="ILogEventEnricher"/> enricher.
    /// The enricher is wrapped in a <see cref="SerilogEnricherAdapter"/> and
    /// registered via <c>QuickLogBuilder.WithEnrichers</c>.
    ///
    /// <para>
    /// <b>Enrichment contract:</b> the user enricher receives an enrichment-time
    /// <see cref="MMP.Herald.Serilog.Events.LogEvent"/> view (not a finalised mirror)
    /// and a <see cref="ILogEventPropertyFactory"/> shim. Properties added via the
    /// factory are forwarded to the native pipeline's
    /// <see cref="MMP.Herald.Events.LogEventEnrichmentContext"/>, where they appear
    /// on the finalised event that reaches sinks.
    /// </para>
    ///
    /// <para>
    /// <b>Known gap — JSON round-trip:</b> the adapter serialises as a bare type name.
    /// A rebuilt pipeline cannot reconstruct the user enricher from JSON alone.
    /// Pinned by <c>CustomEnricherAdapterTests.ToJsonConfig_emits_bare_type_name_known_gap</c>.
    /// </para>
    /// </summary>
    public LoggerConfiguration With(ILogEventEnricher enricher)
    {
        ArgumentNullException.ThrowIfNull(enricher);
        _root.Builder.WithEnrichers(new SerilogEnricherAdapter(enricher));
        return _root;
    }
}
