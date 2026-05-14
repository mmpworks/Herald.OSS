#nullable enable

using System;
using MMP.Herald.Enrichers;
using MMP.Herald.Events;

namespace MMP.Herald.Addons.GameEnrichers;

/// <summary>
/// Attaches build metadata to every log event. Set once at startup.
/// Every crash report service (Sentry, BugSplat, BugSnag) considers this mandatory.
///
/// Fields: buildId, gameVersion, commitHash, buildConfiguration (debug/release).
/// </summary>
public sealed class BuildInfoEnricher : ILogEnricher
{
    private readonly string? _buildId;
    private readonly string? _gameVersion;
    private readonly string? _commitHash;
    private readonly string? _buildConfiguration;

    public BuildInfoEnricher(
        string? buildId = null,
        string? gameVersion = null,
        string? commitHash = null,
        string? buildConfiguration = null) {
        _buildId = buildId;
        _gameVersion = gameVersion;
        _commitHash = commitHash;
        _buildConfiguration = buildConfiguration;
    }

    public void Enrich(LogEventEnrichmentContext context) {
        ArgumentNullException.ThrowIfNull(context);

        if (_buildId is not null) context.SetContextValue(GameContextKeys.BuildId, _buildId);
        if (_gameVersion is not null) context.SetContextValue(GameContextKeys.GameVersion, _gameVersion);
        if (_commitHash is not null) context.SetContextValue(GameContextKeys.CommitHash, _commitHash);
        if (_buildConfiguration is not null) context.SetContextValue(GameContextKeys.BuildConfiguration, _buildConfiguration);
    }
}
