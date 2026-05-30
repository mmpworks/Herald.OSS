#nullable enable

using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Core;

/// <summary>
/// Serilog-compat enricher interface. Consumer enrichers implement this to
/// attach properties to each log event before it reaches any sink.
///
/// <para>
/// <b>Adapter contract (S2 seam):</b> Herald wraps each implementation in a
/// <c>SerilogEnricherAdapter</c> that bridges the call into the native
/// <see cref="MMP.Herald.Enrichers.ILogEnricher"/> pipeline. The user enricher
/// receives a Serilog-shaped <see cref="MMP.Herald.Serilog.Events.LogEvent"/>
/// view and an <see cref="ILogEventPropertyFactory"/> shim. Properties added
/// via the factory are forwarded to the native enrichment context and appear
/// on the finalized event.
/// </para>
///
/// <para>
/// <b>Hard boundary:</b> this interface works only for enrichers compiled from
/// source against <c>MMP.Herald.Serilog</c>. Pre-compiled community packages
/// compiled against real Serilog (PublicKeyToken=24c2f752a8e58a10) will not
/// satisfy this interface.
/// </para>
/// </summary>
public interface ILogEventEnricher
{
    /// <summary>
    /// Enrich the supplied <paramref name="logEvent"/> by adding, updating,
    /// or removing properties via <paramref name="propertyFactory"/>.
    /// </summary>
    void Enrich(
        MMP.Herald.Serilog.Events.LogEvent logEvent,
        ILogEventPropertyFactory propertyFactory);
}
