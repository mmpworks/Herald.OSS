#nullable enable

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing retry policy for a sink.
/// </summary>
public sealed record JsonRetryPolicyConfig(
    bool Enabled,
    int MaxAttempts = 3,
    int DelayMs = 250);