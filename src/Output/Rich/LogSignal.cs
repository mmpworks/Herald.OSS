#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Output.Rich;

/// <summary>
/// An actionable signal embedded in rendered output.
/// Transformers produce signals alongside visual fragments.
/// Signal handlers consume them to trigger side effects -
/// alerts, notifications, game events, external integrations.
///
/// Signals are self-contained: the transformer enriches the payload
/// with whatever context the handler needs, so handlers never
/// depend on logging internals.
///
/// Usage in a transformer:
///   new LogSignal("guard_alert", new Dictionary&lt;string, object?&gt;
///   {
///       ["location"] = "throne_room",
///       ["threat"] = "hostile_player"
///   })
/// </summary>
public sealed record LogSignal(
    string Name,
    IReadOnlyDictionary<string, object?>? Payload = null)
{
    /// <summary>
    /// Retrieve a typed value from the payload, or default if missing.
    /// </summary>
    public T? Get<T>(string key)
    {
        if (Payload is null) return default;
        if (!Payload.TryGetValue(key, out var value)) return default;
        return value is T typed ? typed : default;
    }
}
