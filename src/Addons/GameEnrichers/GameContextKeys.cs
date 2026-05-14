#nullable enable

namespace MMP.Herald.Addons.GameEnrichers;

/// <summary>
/// Context keys used by game-specific enrichers.
/// Use these instead of raw strings when checking enriched context values.
/// </summary>
public static class GameContextKeys
{
    /// <summary>Player ID (set by PlayerEnricher).</summary>
    public const string PlayerId = "playerId";

    /// <summary>Session/match ID (set by SessionEnricher).</summary>
    public const string SessionId = "sessionId";

    /// <summary>Current scene/level/map name (set by SceneEnricher).</summary>
    public const string Scene = "scene";

    /// <summary>Build identifier (set by BuildInfoEnricher).</summary>
    public const string BuildId = "buildId";

    /// <summary>Game version string (set by BuildInfoEnricher).</summary>
    public const string GameVersion = "gameVersion";

    /// <summary>Git commit hash (set by BuildInfoEnricher).</summary>
    public const string CommitHash = "commitHash";

    /// <summary>Build configuration - debug/release (set by BuildInfoEnricher).</summary>
    public const string BuildConfiguration = "buildConfiguration";
}
