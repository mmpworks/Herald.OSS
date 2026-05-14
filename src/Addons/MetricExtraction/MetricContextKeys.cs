#nullable enable

using MMP.Herald.Services;

namespace MMP.Herald.Addons.MetricExtraction;

/// <summary>
/// Context keys used by metric extraction addons.
/// Delegates to LogContextKeys - the single source of truth.
/// </summary>
public static class MetricContextKeys
{
    /// <summary>Duplicate event count within dedup window (set by LogDeduplicationProcessor).</summary>
    public const string DuplicateCount = LogContextKeys.DuplicateCount;
}
