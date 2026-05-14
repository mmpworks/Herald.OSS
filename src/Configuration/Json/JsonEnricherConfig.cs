#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing enricher configuration. Carries the enricher's identity
/// (<see cref="Kind"/>) plus any constructor parameters it needs to be
/// reconstructed (<see cref="Properties"/>).
///
/// <para>
/// Replaces the previous bare-string <c>enrichers: ["ServiceIdentityEnricher"]</c>
/// shape. The bare-string form dropped every constructor parameter — a
/// <c>ServiceIdentityEnricher("payments-api", version: "2.1")</c> deserialized
/// without its <c>serviceName</c> or <c>version</c>, leaving events unstamped
/// and per-pipeline filters silently dropping them.
/// </para>
///
/// <para>
/// <see cref="Properties"/> is a polymorphic dict so each enricher kind owns
/// its own shape. The runtime mapper hands the dict to
/// <c>EnricherJsonRegistry.Reconstruct(...)</c>, which looks up <see cref="Kind"/>
/// and asks the registered factory to build the enricher.
/// </para>
/// </summary>
public sealed record JsonEnricherConfig(
    string Kind,
    IReadOnlyDictionary<string, object?>? Properties = null);
