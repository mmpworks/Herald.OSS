#nullable enable

using System.Collections.Generic;
using System.IO;

namespace MMP.Herald.Configuration;

/// <summary>
/// One entry in the dashboard pipeline catalog — the unified shape that
/// combines kernel-companion or strategy-step metadata with the embedded
/// <c>.mmpform</c> text. A server's <c>/api/pipeline/catalog</c> endpoint
/// returns a list of these.
///
/// <para>
/// The Herald.OSS port intentionally omits the richer <c>PipelineCapability</c>
/// / <c>PipelineCapabilityMatrix</c> shape from the in-tree Core variant —
/// no paid consumer reads those types, and keeping them out of OSS avoids
/// shipping internal scaffolding that has no public contract. The catalog
/// item itself remains the wire-format type consumers serialize to JSON.
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
/// Builds the unified catalog list. One server call site
/// (<c>/api/pipeline/catalog</c>) reads it; consumers serialise the
/// resulting list to JSON. Static + immutable inputs mean no caching
/// is needed — the build cost is negligible (constant-size kernel
/// companion list + the registered strategy step set).
///
/// <para>
/// Manifest text is loaded from embedded resources under
/// <c>manifests/pipeline/</c> — the same naming convention the in-tree
/// dashboard catalog uses. When a manifest file is not embedded the
/// catalog item's <see cref="PipelineCatalogItem.FormSchemaText"/> stays
/// <c>null</c>; the consumer renders the badge and metadata without a
/// configuration form.
/// </para>
/// </summary>
public static class PipelineCatalog
{
    private const string ResourcePrefix = "manifests/pipeline/";
    private const string StepStubResource = ResourcePrefix + "step-stub.mmpform";

    // Kernel-companion entries — feature key + dashboard-facing metadata.
    // Reference cost numbers track the in-tree Core
    // PipelineCapabilityMatrix values, so the catalog the Server emits
    // matches the published kernel-fast-path documentation. Tier is
    // Community across the board — the family ships as the 1.0 headline
    // and is available everywhere.
    private static readonly KernelCompanionEntry[] _kernelCompanions =
    [
        new("fastRedaction", "Fast Redaction",
            KernelCost: new PipelineCatalogCost(92, 344),
            ChainCost: new PipelineCatalogCost(869, 1648),
            UpgradePitch: "Fast redaction shaves ~89% wall-clock and ~79% allocation off the chain redactor on the configured-but-inert path."),

        new("fastSampling", "Fast Sampling",
            KernelCost: new PipelineCatalogCost(26, 0),
            ChainCost: new PipelineCatalogCost(687, 1088),
            UpgradePitch: "Fast sampling drops at ~13 ns / 0 B — cheaper than the no-sampling baseline because there's no event to allocate."),

        new("fastEnrichment", "Fast Enrichment",
            KernelCost: new PipelineCatalogCost(138, 136),
            ChainCost: new PipelineCatalogCost(939, 1832),
            UpgradePitch: "Fast enrichment for static properties (host.name, service.name) cuts ~85% wall-clock and ~93% allocation versus the chain enricher."),

        new("fastDynamicLevel", "Fast Dynamic Level",
            KernelCost: new PipelineCatalogCost(30, 0),
            ChainCost: new PipelineCatalogCost(663, 1088),
            UpgradePitch: "Fast dynamic level reads through a LogLevelSwitch on the kernel path — ~30 ns / 0 B per accepted event."),

        new("fastAsyncSink", "Fast Async Sink",
            KernelCost: new PipelineCatalogCost(233, 307),
            ChainCost: new PipelineCatalogCost(1002, 1221),
            UpgradePitch: "Fast async sink wraps a Channel<LogEvent> on the kernel path — ~77% wall-clock and ~75% allocation reduction per accepted event."),
    ];

    /// <summary>
    /// Build the full catalog: every kernel companion plus every
    /// registered strategy step, with form-schema text attached when an
    /// embedded manifest is present.
    /// </summary>
    public static IReadOnlyList<PipelineCatalogItem> Build()
    {
        var items = new List<PipelineCatalogItem>();

        foreach (var companion in _kernelCompanions)
        {
            items.Add(BuildKernelCompanionItem(companion));
        }

        // Strategy steps are pulled from PipelineStep at build time so
        // adding a new step (built-in or plugin-supplied via Register)
        // flows through to the catalog without a matrix-side change.
        foreach (var name in PipelineStep.AllNames)
        {
            var step = PipelineStep.FromName(name);
            if (step is null) continue;

            // Skip if the name collides with a kernel companion key —
            // the companion entry wins (more specific cost data).
            if (IsKernelCompanionKey(step.Name)) continue;

            items.Add(BuildStrategyStepItem(step));
        }

        return items;
    }

    private static PipelineCatalogItem BuildKernelCompanionItem(KernelCompanionEntry entry) =>
        new(
            FeatureKey: entry.FeatureKey,
            DisplayName: entry.DisplayName,
            Kind: "KernelCompanion",
            MinimumEdition: HeraldEdition.Community.Name,
            ForcesChain: false,
            FallbackStrategy: "GracefulDegrade",
            KernelCost: entry.KernelCost,
            ChainCost: entry.ChainCost,
            UpgradePitch: entry.UpgradePitch,
            Description: null,
            Help: null,
            Vendor: Pipeline.VendorInfo.MMP.Name,
            LinkType: null,
            FormSchemaText: TryReadManifest(ResourcePrefix + "kernel-companion-" + entry.FeatureKey + ".mmpform"));

    private static PipelineCatalogItem BuildStrategyStepItem(PipelineStep step) =>
        new(
            FeatureKey: step.Name,
            DisplayName: step.DisplayName,
            Kind: "StrategyStep",
            MinimumEdition: step.MinimumEdition.Name,
            ForcesChain: true,
            // Match the in-tree fallback convention: Community-tier steps
            // are omitted on lower tiers (a no-op), paid-tier steps throw
            // so the boot-time mistake surfaces with an actionable message.
            FallbackStrategy: step.MinimumEdition == HeraldEdition.Community ? "Omit" : "Throw",
            KernelCost: null,
            ChainCost: null,
            UpgradePitch: string.Empty,
            Description: step.Description,
            Help: step.Help,
            Vendor: step.Vendor.Name,
            LinkType: step.LinkType,
            // Strategy steps fall through to the shared stub when the
            // bespoke per-step manifest is missing, so the dashboard's
            // left-side picker always has something to render.
            FormSchemaText: TryReadManifest(ResourcePrefix + "step-" + step.Name + ".mmpform") ?? TryReadManifest(StepStubResource));

    private static bool IsKernelCompanionKey(string name)
    {
        foreach (var entry in _kernelCompanions)
        {
            if (entry.FeatureKey == name) return true;
        }
        return false;
    }

    private static string? TryReadManifest(string resourceName)
    {
        var assembly = typeof(PipelineCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed record KernelCompanionEntry(
        string FeatureKey,
        string DisplayName,
        PipelineCatalogCost KernelCost,
        PipelineCatalogCost ChainCost,
        string UpgradePitch);
}
