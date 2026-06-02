#nullable enable

// Layer-2 mirror of Serilog.Configuration.LoggerEnrichmentConfiguration.
// Wraps Layer-1 MMP.Herald.Serilog.Configuration.LoggerEnrichmentConfiguration.
// Every method is a one-line forward.
//
// Layer-1 twin: MMP.Herald.Serilog.Configuration.LoggerEnrichmentConfiguration
//
// ILogEventEnricher bridge:
//   L2 Serilog.Core.ILogEventEnricher and L1 MMP.Herald.Serilog.Core.ILogEventEnricher
//   are separate interfaces. L2EnricherBridge adapts L2→L1 so With() compiles.

using System;
using System.Diagnostics.CodeAnalysis;
using L1Config  = MMP.Herald.Serilog.Configuration;
using L1Core    = MMP.Herald.Serilog.Core;
using L1Events  = MMP.Herald.Serilog.Events;

namespace Serilog.Configuration;

/// <summary>
/// Fluent <c>Enrich.*</c> configuration surface for the Layer-2
/// <see cref="Serilog.LoggerConfiguration"/> wrapper.
/// </summary>
public sealed class LoggerEnrichmentConfiguration
{
    private readonly Serilog.LoggerConfiguration _root;
    private readonly L1Config.LoggerEnrichmentConfiguration _inner;

    internal LoggerEnrichmentConfiguration(
        Serilog.LoggerConfiguration root,
        L1Config.LoggerEnrichmentConfiguration inner)
    {
        _root  = root;
        _inner = inner;
    }

    /// <summary>Add a constant-value enrichment property to every log event.</summary>
    public Serilog.LoggerConfiguration WithProperty(string name, object? value, bool destructureObjects = false)
    {
        _inner.WithProperty(name, value, destructureObjects);
        return _root;
    }

    /// <summary>
    /// No-op — Layer-1 wires async-local scope on every build.
    /// Accepted for API compatibility.
    /// </summary>
    public Serilog.LoggerConfiguration FromLogContext()
    {
        _inner.FromLogContext();
        return _root;
    }

    /// <summary>
    /// Register a user-authored <see cref="Core.ILogEventEnricher"/>.
    /// Bridged into the Layer-1 enricher adapter chain via <c>L2EnricherBridge</c>.
    /// </summary>
    public Serilog.LoggerConfiguration With(Core.ILogEventEnricher enricher)
    {
        ArgumentNullException.ThrowIfNull(enricher);
        _inner.With(new L2EnricherBridge(enricher));
        return _root;
    }

    // Adapts a Layer-2 Serilog.Core.ILogEventEnricher into the Layer-1
    // MMP.Herald.Serilog.Core.ILogEventEnricher contract.
    private sealed class L2EnricherBridge : L1Core.ILogEventEnricher
    {
        private readonly Core.ILogEventEnricher _l2Enricher;
        internal L2EnricherBridge(Core.ILogEventEnricher l2Enricher) => _l2Enricher = l2Enricher;

        public void Enrich(L1Events.LogEvent logEvent, L1Core.ILogEventPropertyFactory propertyFactory)
            => _l2Enricher.Enrich(new Events.LogEvent(logEvent), new L2PropertyFactoryBridge(propertyFactory));
    }

    // Adapts a Layer-1 ILogEventPropertyFactory into the Layer-2 contract.
    private sealed class L2PropertyFactoryBridge : Core.ILogEventPropertyFactory
    {
        private readonly L1Core.ILogEventPropertyFactory _inner;
        internal L2PropertyFactoryBridge(L1Core.ILogEventPropertyFactory inner) => _inner = inner;

        [RequiresUnreferencedCode("Value projection uses reflection on arbitrary user types.")]
        public Events.LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
        {
            var l1Prop = _inner.CreateProperty(name, value, destructureObjects);
            return Events.LogEventProperty.FromL1(l1Prop);
        }
    }
}
