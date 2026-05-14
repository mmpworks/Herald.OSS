#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing custom-pipeline-decorator configuration. Carries the
/// decorator's identity (<see cref="Kind"/>) plus the values its
/// <c>ApplyConfiguration</c> consumes (<see cref="Properties"/>).
///
/// <para>
/// Pre-refactor, custom decorators were passed only as in-memory
/// instances to <c>JsonConfiguredLoggingBootstrapFactory.Create</c> and
/// never appeared in the JSON. A Lean deploy reading the same JSON got
/// a chain missing the host's custom decorators entirely — silent
/// functional divergence between the code-built and JSON-loaded paths.
/// </para>
///
/// <para>
/// Mirrors <see cref="JsonEnricherConfig"/>: <see cref="Kind"/> identifies
/// the decorator type for the registry lookup, and the runtime mapper /
/// hot-reload bootstrap reconstructs each entry through
/// <c>DecoratorJsonRegistry.Reconstruct(...)</c>.
/// </para>
/// </summary>
public sealed record JsonPipelineDecoratorConfig(
    string Kind,
    IReadOnlyDictionary<string, object?>? Properties = null);
