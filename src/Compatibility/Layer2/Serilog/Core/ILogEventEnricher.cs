#nullable enable

// Layer-2 mirror of Serilog.Core.ILogEventEnricher.
// Uses Serilog.Events.LogEvent (L2) and Serilog.Core.ILogEventPropertyFactory (L2)
// so consumer enricher classes see the correct types in their method signatures.
//
// Layer-1 twin: MMP.Herald.Serilog.Core.ILogEventEnricher

namespace Serilog.Core;

/// <summary>
/// Serilog-compatible enricher contract exported under the <c>Serilog</c> assembly identity.
/// A consumer's <c>class MyEnricher : Serilog.Core.ILogEventEnricher</c> compiles unchanged
/// after swapping the Serilog package for this assembly.
/// </summary>
public interface ILogEventEnricher
{
    /// <summary>
    /// Enrich <paramref name="logEvent"/> by adding, updating, or removing properties
    /// via <paramref name="propertyFactory"/>.
    /// </summary>
    void Enrich(Events.LogEvent logEvent, ILogEventPropertyFactory propertyFactory);
}
