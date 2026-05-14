#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Quick.Serializers;

/// <summary>
/// Emits the config-properties dictionary for a single pipeline step.
/// One implementation per step kind (async, batching, filtering, etc.).
/// Owns the mapping from builder state to the JSON config shape so the
/// knowledge lives next to the step, not in a central switch statement.
/// </summary>
public interface IStepConfigSerializer
{
    /// <summary>
    /// The step name this serializer handles. Matches
    /// <see cref="Configuration.PipelineStep"/>.Name.
    /// </summary>
    string StepName { get; }

    /// <summary>
    /// Build the step's config dictionary from the builder's current state.
    /// Return <c>null</c> when the step has no configurable properties.
    /// </summary>
    Dictionary<string, object?>? BuildConfig(QuickLogBuilder builder);
}
