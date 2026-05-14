#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing configuration for log event sampling and throttling.
/// </summary>
public sealed record JsonSamplingConfig(
    bool Enabled = false,
    IReadOnlyList<JsonSamplingRule>? Rules = null);

/// <summary>
/// JSON-facing sampling rule. At least one of SampleRate or MaxPerSecond should be set.
/// Category and MessageContains are optional scope narrowing fields.
/// </summary>
public sealed record JsonSamplingRule(
    string? Category = null,
    string? MessageContains = null,
    int SampleRate = 1,
    int MaxPerSecond = 0);
