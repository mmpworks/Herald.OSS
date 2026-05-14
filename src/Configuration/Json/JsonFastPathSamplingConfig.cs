#nullable enable

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing configuration for the kernel-aware
/// <see cref="MMP.Herald.Pipeline.Kernel.FastPathSampler"/>. Carries the
/// 1-in-N rate through the JSON round-trip.
///
/// <para>
/// Scope mirrors the runtime path: pure 1-in-N counter sampling. Per-
/// category scopes, max-per-second throttling, and consistent-hash
/// shapes stay on the legacy <see cref="JsonSamplingConfig"/>.
/// </para>
/// </summary>
public sealed record JsonFastPathSamplingConfig(int SampleRate);
