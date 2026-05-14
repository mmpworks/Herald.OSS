#nullable enable

using System;
using MMP.Herald.Events;
using MMP.Herald.Enrichers;

namespace MMP.Herald.Addons.GameEnrichers;

/// <summary>
/// Attaches a session identifier to every log event.
/// Set once at session/match start. Immutable after construction.
/// Useful for correlating all logs from a single gameplay session
/// across client and server instances.
/// </summary>
public sealed class SessionEnricher : ILogEnricher
{
    private readonly string _sessionId;

    public SessionEnricher(string sessionId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        _sessionId = sessionId;
    }

    public void Enrich(LogEventEnrichmentContext context) {
        ArgumentNullException.ThrowIfNull(context);
        context.SetContextValue(GameContextKeys.SessionId, _sessionId);
    }
}
