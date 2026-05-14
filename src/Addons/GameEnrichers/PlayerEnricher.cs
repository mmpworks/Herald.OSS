#nullable enable

using System;
using MMP.Herald.Events;
using MMP.Herald.Enrichers;

namespace MMP.Herald.Addons.GameEnrichers;

/// <summary>
/// Attaches a player identifier to every log event.
/// Mutable: player ID changes on login/logout/character switch.
/// Thread-safe via volatile field.
/// </summary>
public sealed class PlayerEnricher : ILogEnricher
{
    private volatile string? _playerId;

    public PlayerEnricher(string? initialPlayerId = null) {
        _playerId = initialPlayerId;
    }

    /// <summary>
    /// Update the current player ID. Thread-safe.
    /// Pass null to clear (logged out state).
    /// </summary>
    public void SetPlayerId(string? playerId) {
        _playerId = playerId;
    }

    public void Enrich(LogEventEnrichmentContext context) {
        ArgumentNullException.ThrowIfNull(context);
        var id = _playerId;
        if (id is not null)
        {
            context.SetContextValue(GameContextKeys.PlayerId, id);
        }
    }
}
