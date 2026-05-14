#nullable enable

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing configuration for the kernel-aware
/// <see cref="MMP.Herald.Pipeline.Kernel.FastPathAsyncSink"/>. Carries
/// the channel's bounded capacity — the back-pressure threshold above
/// which the producer drops events instead of blocking.
///
/// <para>
/// Reload semantics: every reload retires the old async sink (drain +
/// dispose) and installs a new one with the JSON-supplied capacity.
/// In-flight events on the old sink finish on the old inner; post-
/// reload events route through the new sink. Operators editing the
/// JSON capacity see the change after the next reload.
/// </para>
///
/// <para>
/// Scope: a single FastPathAsyncSink wrapping the entire
/// <see cref="MMP.Herald.Pipeline.SafeCompositeLogger"/> sink fan-out
/// for this pipeline. One channel, one consumer thread, one buffer.
/// Per-sink wrappers (one channel per configured sink) are a separate
/// design covered in <c>docs/future-direction.md</c>.
/// </para>
/// </summary>
public sealed record JsonFastPathAsyncSinkConfig(int BoundedCapacity);
