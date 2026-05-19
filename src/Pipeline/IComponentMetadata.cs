#nullable enable

using System.Collections.Generic;
using MMP.Herald.Routing;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Self-describing metadata for any pipeline component — built-in or plugin.
///
/// The metadata travels WITH the class that owns it. When the component changes,
/// the metadata changes with it. The Dashboard queries the component directly,
/// not a separate registry.
///
/// Built-in components (AsyncLogger, FilteringLogger, etc.) implement this
/// alongside ILogger. Plugin components implement it via
/// IConfigurablePipelineDecorator (which extends this).
///
/// PipelineStep becomes a lightweight name — the rich metadata lives here.
///
/// Usage by Dashboard:
///   var component = result.Pipeline.Get&lt;AsyncLogger&gt;();
///   if (component is IComponentMetadata meta)
///   {
///       Console.WriteLine(meta.ComponentName);   // "async"
///       Console.WriteLine(meta.DisplayName);     // "Async Queue"
///       Console.WriteLine(meta.Description);     // "Offloads log events..."
///       Console.WriteLine(meta.Help);            // Multi-paragraph explanation
///       Console.WriteLine(meta.Vendor);          // "MMP"
///   }
/// </summary>
public interface IComponentMetadata
{
    /// <summary>
    /// Machine-readable component identifier. For pipeline steps, matches the
    /// PipelineStep name (e.g., "async", "filtering", "rateLimiter").
    /// </summary>
    string ComponentName { get; }

    /// <summary>Human-readable display name shown in the Dashboard.</summary>
    string DisplayName { get; }

    /// <summary>One-line description of what the component does.</summary>
    string Description { get; }

    /// <summary>
    /// Multi-paragraph help text with placement advice, configuration tips,
    /// and usage examples. Rendered in the Dashboard's help panel.
    /// </summary>
    string Help { get; }

    /// <summary>
    /// Rich vendor metadata: name, license, copyright, contact info.
    /// Built-in components use VendorInfo.MMP. Plugins provide their own.
    /// The Dashboard renders this in the component detail panel.
    /// </summary>
    VendorInfo Vendor { get; }

    /// <summary>
    /// Configuration schema for Dashboard rendering. Empty list if not configurable.
    /// Built-in components return their read-only inspection fields.
    /// Plugin components return their mutable configuration fields.
    /// </summary>
    IReadOnlyList<SinkConfigField> ConfigurationSchema { get; }

    /// <summary>
    /// Pipeline placement rules owned by this component.
    /// Defines optimal position, incompatibilities, preferences, and requirements.
    /// The validation system reads these when checking strategy correctness.
    /// Default: no constraints.
    /// </summary>
    Configuration.PipelineStepRules Rules => Configuration.PipelineStepRules.Default;

    /// <summary>
    /// Minimum Herald edition required to use this component.
    /// Default: <see cref="HeraldEdition.Community"/> (available in all editions).
    /// Paid pipeline decorators (Pro/Enterprise) override this with an
    /// explicit implementation so the host can validate the running edition
    /// against every decorator a strategy names.
    /// </summary>
    HeraldEdition MinimumEdition => HeraldEdition.Community;
}
