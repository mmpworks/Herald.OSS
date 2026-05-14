#nullable enable

namespace MMP.Herald.Services;

/// <summary>
/// Default values for pipeline configuration.
/// Use these instead of magic numbers when constructing pipeline components.
/// </summary>
public static class PipelineDefaults
{
    /// <summary>Default batch size for BatchingLogger and related components.</summary>
    public const int BatchSize = 32;

    /// <summary>Default batch delay in milliseconds.</summary>
    public const int BatchDelayMs = 1000;

    /// <summary>Default async queue capacity.</summary>
    public const int AsyncCapacity = 1024;

    /// <summary>Default WAL max size in bytes (10 MB).</summary>
    public const long WalMaxBytes = 10 * 1024 * 1024;
}
