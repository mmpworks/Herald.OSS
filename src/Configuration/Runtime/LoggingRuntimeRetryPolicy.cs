#nullable enable

namespace MMP.Herald.Configuration.Runtime;
/// <summary>
/// Runtime retry policy normalized from transport config.
/// A non-null instance means retry is enabled for the sink.
/// </summary>
public sealed record LoggingRuntimeRetryPolicy(
    int MaxAttempts,
    int DelayMs);