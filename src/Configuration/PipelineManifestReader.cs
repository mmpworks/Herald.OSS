#nullable enable

using System;
using System.IO;
using System.Reflection;

namespace MMP.Herald.Configuration;

/// <summary>
/// Reads the embedded <c>.mmpform</c> manifests Core ships for kernel
/// companions and pipeline strategy steps. Files live under
/// <c>manifests/pipeline/</c> in the source tree and are embedded by
/// <c>Herald.Core.csproj</c>'s wildcard EmbeddedResource clause.
///
/// <para>
/// <b>Naming.</b> Two prefixes today:
/// </para>
/// <list type="bullet">
///   <item><c>kernel-companion-{featureKey}.mmpform</c> — one per
///         kernel-aware companion (fastRedaction, fastSampling,
///         fastEnrichment, fastDynamicLevel, fastAsyncSink).</item>
///   <item><c>step-{stepName}.mmpform</c> — one per strategy step.
///         When a per-step file is missing the reader falls through
///         to <c>step-stub.mmpform</c> so every step in the matrix
///         has a renderable form, even before its bespoke schema is
///         authored.</item>
/// </list>
///
/// <para>
/// <b>Why a fallback stub?</b> The dashboard's contract with Core is
/// "every catalog item ships a form text." Without a fallback, every
/// new strategy step would block the dashboard's left-side picker
/// until someone authored a real schema. The stub keeps the wireup
/// honest while making "fill in real forms" purely additive — a
/// future contributor drops <c>step-{stepName}.mmpform</c> into
/// <c>manifests/pipeline/</c> and the reader picks it up on the next
/// rebuild.
/// </para>
/// </summary>
public static class PipelineManifestReader
{
    private const string ResourcePrefix = "manifests/pipeline/";
    private const string StepStubResource = ResourcePrefix + "step-stub.mmpform";

    /// <summary>
    /// Read the manifest text for a feature key. Looks up:
    ///
    /// <list type="number">
    ///   <item>
    ///     <c>kernel-companion-{featureKey}.mmpform</c> when the entry
    ///     is a kernel companion.
    ///   </item>
    ///   <item>
    ///     <c>step-{featureKey}.mmpform</c> when the entry is a strategy
    ///     step.
    ///   </item>
    ///   <item>
    ///     <c>step-stub.mmpform</c> as the fallback for strategy steps
    ///     that don't yet ship a bespoke form.
    ///   </item>
    /// </list>
    ///
    /// Returns <c>null</c> only when no resource matches and the fallback
    /// is also missing — which would be a build-time bug, since the stub
    /// ships in the Core assembly.
    /// </summary>
    public static string? ReadForCapability(PipelineCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);

        var primary = ResourceNameFor(capability);
        var text = TryReadResource(primary);
        if (text is not null) return text;

        // Strategy steps fall through to the shared stub. Kernel
        // companions don't — every companion has its own bespoke form
        // shipped today, and a missing companion file is a real bug
        // worth surfacing as null.
        if (capability.Kind == CapabilityKind.StrategyStep)
        {
            return TryReadResource(StepStubResource);
        }

        return null;
    }

    /// <summary>
    /// Read a manifest by raw resource name (e.g.
    /// <c>"manifests/pipeline/step-stub.mmpform"</c>). Returns
    /// <c>null</c> when no embedded resource matches. Exposed for
    /// callers that need direct access to a specific manifest file
    /// outside the capability lookup path.
    /// </summary>
    public static string? TryReadResource(string resourceName)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceName);

        var assembly = typeof(PipelineManifestReader).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Compute the embedded-resource name a capability would read from.
    /// Public so the catalog endpoint can advertise the resource name
    /// in its diagnostic output without re-implementing the rule.
    /// </summary>
    public static string ResourceNameFor(PipelineCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        var prefix = capability.Kind == CapabilityKind.KernelCompanion
            ? "kernel-companion-"
            : "step-";
        return $"{ResourcePrefix}{prefix}{capability.FeatureKey}.mmpform";
    }
}
