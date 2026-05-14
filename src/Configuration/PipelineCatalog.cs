#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Configuration;

/// <summary>
/// One entry in the dashboard pipeline catalog — the unified shape
/// that combines <see cref="PipelineCapabilityMatrix"/> data, optional
/// <see cref="PipelineStep"/> metadata, and the embedded
/// <c>.mmpform</c> text. The Server's
/// <c>/api/pipeline/catalog</c> endpoint returns a list of these.
///
/// <para>
/// <b>What lives here vs in <see cref="PipelineCapability"/>.</b>
/// PipelineCapability is the engineering shape — tier, costs, fallback
/// policy. PipelineCatalogItem adds the dashboard-facing extras:
/// description, help text, manifest text, vendor info. Keeping them
/// separate lets non-dashboard consumers (the recommender, the
/// diagnostic shape) read the matrix without paying for manifest I/O.
/// </para>
/// </summary>
public sealed record PipelineCatalogItem(
    string FeatureKey,
    string DisplayName,
    string Kind,
    string MinimumEdition,
    bool ForcesChain,
    string FallbackStrategy,
    PipelineCatalogCost? KernelCost,
    PipelineCatalogCost? ChainCost,
    string UpgradePitch,
    string? Description,
    string? Help,
    string? Vendor,
    string? LinkType,
    string? FormSchemaText);

/// <summary>JSON-friendly cost shape (no struct, nullable members).</summary>
public sealed record PipelineCatalogCost(double Nanoseconds, int Bytes);

/// <summary>
/// Builds the unified catalog list. One call site
/// (<c>/api/pipeline/catalog</c>) reads it; consumers serialise the
/// resulting list to JSON. Static + immutable inputs mean no caching
/// is needed — the build cost is negligible (constant-size matrix +
/// constant-size step list).
/// </summary>
public static class PipelineCatalog
{
    /// <summary>
    /// Build the full catalog: every kernel companion plus every
    /// strategy step, with form-schema text attached.
    /// </summary>
    public static IReadOnlyList<PipelineCatalogItem> Build()
    {
        var items = new List<PipelineCatalogItem>(PipelineCapabilityMatrix.All.Count);

        foreach (var capability in PipelineCapabilityMatrix.All)
        {
            items.Add(BuildItem(capability));
        }

        return items;
    }

    /// <summary>Build a single item — exposed for endpoint-level filtering.</summary>
    public static PipelineCatalogItem BuildItem(PipelineCapability capability)
    {
        var step = capability.Kind == CapabilityKind.StrategyStep
            ? PipelineStep.FromName(capability.FeatureKey)
            : null;

        return new PipelineCatalogItem(
            FeatureKey: capability.FeatureKey,
            DisplayName: capability.DisplayName,
            Kind: capability.Kind.ToString(),
            MinimumEdition: capability.TierRequirement.Name,
            ForcesChain: capability.ForcesChain,
            FallbackStrategy: capability.FallbackStrategy.ToString(),
            KernelCost: capability.KernelCost.HasValue
                ? new PipelineCatalogCost(capability.KernelCost.Nanoseconds, capability.KernelCost.Bytes)
                : null,
            ChainCost: capability.ChainCost is { HasValue: true } chain
                ? new PipelineCatalogCost(chain.Nanoseconds, chain.Bytes)
                : null,
            UpgradePitch: capability.UpgradePitch,
            Description: step?.Description,
            Help: step?.Help,
            Vendor: step?.Vendor.Name,
            LinkType: step?.LinkType,
            FormSchemaText: PipelineManifestReader.ReadForCapability(capability));
    }
}
