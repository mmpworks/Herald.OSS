#nullable enable

using System;
using MMP.Herald.Enrichers;
using MMP.Herald.Events;

namespace MMP.Herald.Addons.GameEnrichers;

/// <summary>
/// Attaches the current game scene/level/map name to every log event.
/// Mutable: scene changes during gameplay. Thread-safe via volatile.
///
/// When reviewing crash logs, knowing the player was on "Level3_BossArena"
/// vs "MainMenu" is essential context.
/// </summary>
public sealed class SceneEnricher : ILogEnricher
{
    private volatile string? _sceneName;

    public SceneEnricher(string? initialScene = null) {
        _sceneName = initialScene;
    }

    /// <summary>
    /// Update the current scene. Call on scene transitions.
    /// Pass null to clear (e.g., during loading screens).
    /// </summary>
    public void SetScene(string? sceneName) {
        _sceneName = sceneName;
    }

    public void Enrich(LogEventEnrichmentContext context) {
        ArgumentNullException.ThrowIfNull(context);
        var scene = _sceneName;
        if (scene is not null)
        {
            context.SetContextValue(GameContextKeys.Scene, scene);
        }
    }
}
