#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing configuration for log event sampling, throttling, and adaptive sampling.
/// </summary>
public sealed record JsonSamplingConfig(
    bool Enabled = false,
    IReadOnlyList<JsonSamplingRule>? Rules = null);

/// <summary>
/// JSON-facing sampling rule. Exactly one sampling MODE is expressed per rule, chosen by
/// which field is set (checked in this precedence): adaptive (when
/// <see cref="AdaptiveNormalSampleRate"/> &gt; 0), throttling (when
/// <see cref="MaxPerSecond"/> &gt; 0), else fixed-rate sampling
/// (<see cref="SampleRate"/>). <see cref="Category"/> and <see cref="MessageContains"/>
/// optionally narrow the rule's scope.
/// </summary>
/// <param name="Category">Optional scope: only events in this category match the rule.</param>
/// <param name="MessageContains">Optional scope: only events whose message contains this substring match.</param>
/// <param name="SampleRate">Fixed-rate sampling: keep 1 in every N events. Used when no other mode field is set.</param>
/// <param name="MaxPerSecond">Throttling: cap at N events per second. Takes precedence over <see cref="SampleRate"/>.</param>
/// <param name="AdaptiveNormalSampleRate">
/// Adaptive sampling: the keep-1-in-N rate during normal (low-error) periods. When &gt; 0
/// this rule is adaptive and <see cref="AdaptiveErrorThreshold"/> applies. Takes precedence
/// over throttling and fixed-rate. Adaptive captures everything during error spikes.
/// </param>
/// <param name="AdaptiveErrorThreshold">
/// Adaptive sampling: error count within the window that flips the sampler to keep-all.
/// Only meaningful when <see cref="AdaptiveNormalSampleRate"/> &gt; 0.
/// </param>
/// <param name="AdaptiveWindowMs">
/// Adaptive sampling: the sliding error-count window in milliseconds. Defaults to 1000
/// (one second) when 0. Only meaningful when <see cref="AdaptiveNormalSampleRate"/> &gt; 0.
/// </param>
public sealed record JsonSamplingRule(
    string? Category = null,
    string? MessageContains = null,
    int SampleRate = 1,
    int MaxPerSecond = 0,
    int AdaptiveNormalSampleRate = 0,
    int AdaptiveErrorThreshold = 0,
    int AdaptiveWindowMs = 0);
