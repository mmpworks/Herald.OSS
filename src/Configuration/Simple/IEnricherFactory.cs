#nullable enable

using MMP.Herald.Enrichers;

namespace MMP.Herald.Configuration.Simple;

/// <summary>
/// Produces an <see cref="ILogEnricher"/> from a simple-schema pipeline
/// config. One implementation per enricher kind. The factory decides
/// whether the enricher applies at all — returning <c>null</c> marks the
/// kind as a no-op placeholder (for example, the default machine / process /
/// thread enrichers already run, so their factories just acknowledge the
/// name and return null without adding a second instance).
/// </summary>
public interface IEnricherFactory
{
    /// <summary>
    /// The vocabulary name this factory handles (case-insensitive), matching
    /// the strings callers write in <c>SimplePipelineConfig.Enrichers</c>.
    /// </summary>
    string EnricherName { get; }

    /// <summary>
    /// Build the enricher instance (or return null to acknowledge the name
    /// without registering anything). Invoked once per pipeline during
    /// <see cref="SimpleConfigFactory.Build"/>.
    /// </summary>
    ILogEnricher? Create(SimplePipelineConfig pipeline);
}
