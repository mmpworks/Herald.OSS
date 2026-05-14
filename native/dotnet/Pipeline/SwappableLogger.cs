#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using MMP.Herald.Events;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Decorator that holds a volatile reference to an inner ILogger pipeline.
/// Enables atomic pipeline replacement for hot reload scenarios.
/// Inserted between StructuredLogger and the rest of the decorator chain.
/// </summary>
public sealed class SwappableLogger : ILogger, IDescribable, IComponentMetadata
{
    private ILogger _inner;

    public SwappableLogger(ILogger inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public void Log(LogEvent logEvent)
    {
        Volatile.Read(ref _inner).Log(logEvent);
    }

    // Async path takes the same volatile snapshot so a concurrent SwapInner
    // either hits the old pipeline completely or the new one — never a
    // half-swapped chain.
    public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default) =>
        Volatile.Read(ref _inner).LogAsync(logEvent, cancellationToken);

    /// <summary>
    /// Atomically swaps the inner pipeline. Returns the old inner for disposal.
    /// In-flight events on the old pipeline complete normally since the old chain
    /// remains valid until explicitly disposed.
    /// </summary>
    public ILogger SwapInner(ILogger newInner)
    {
        ArgumentNullException.ThrowIfNull(newInner);
        return Interlocked.Exchange(ref _inner, newInner);
    }

    /// <summary>
    /// Returns the current inner pipeline.
    /// </summary>
    public ILogger Current => Volatile.Read(ref _inner);
    public string Describe() => "SwappableLogger";

    // -- IComponentMetadata (single source of truth for this component's identity and rules) --

    /// <summary>Pipeline placement rules owned by this class.</summary>
    internal static readonly Configuration.PipelineStepRules StepRules = new(
        OptimalPosition: ["first"],
        IncompatibleWith: ["hotPath"],
        MoreInfo: new System.Collections.Generic.Dictionary<string, string> {
            ["optimalPosition"] = "HotReload must wrap the entire pipeline to enable atomic replacement.",
            ["incompatibleWith"] = "HotReload and HotPath are mutually exclusive entry points."
        });

    string IComponentMetadata.ComponentName => "swappable";
    string IComponentMetadata.DisplayName => "Entry Point: Hot-Reload";
    string IComponentMetadata.Description => "Enables hot-reload pipeline swapping at runtime.";
    string IComponentMetadata.Help => "The HotReload entry point wraps the entire pipeline in a swappable container. When you call RebuildFrom(builder), the old pipeline is atomically replaced. In-flight events complete on the old pipeline; new events go to the new one. Place as the outermost step.";
    VendorInfo IComponentMetadata.Vendor => VendorInfo.MMP;
    System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> IComponentMetadata.ConfigurationSchema => [];
    Configuration.PipelineStepRules IComponentMetadata.Rules => StepRules;
}
