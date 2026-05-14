#nullable enable

using System.Reflection;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Test- and diagnostics-friendly introspection over a
/// <see cref="StructuredLogger"/>. Reports whether the kernel fast path is
/// wired for the given logger. Uses reflection on a known private field so
/// the StructuredLogger public surface stays unchanged.
///
/// Intended for tests, diagnostics, and dashboard surfaces — not for the
/// hot path. Reflection cost is amortised over the lifetime of the logger.
/// </summary>
public static class KernelIntrospection
{
    private static readonly FieldInfo? KernelField = typeof(StructuredLogger)
        .GetField("_kernel", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>
    /// True when the supplied logger was built with a kernel-eligible
    /// configuration and will use the fast path for qualifying calls.
    /// </summary>
    public static bool HasKernel(StructuredLogger logger)
    {
        System.ArgumentNullException.ThrowIfNull(logger);
        return KernelField?.GetValue(logger) is not null;
    }

    /// <summary>
    /// Set by <c>DefaultLogPipelineFactory.TryCompileKernel</c> on the most
    /// recent kernel-compile attempt. Contains the human-readable reason from
    /// <see cref="KernelEligibility.DescribeRejection"/>, <c>null</c> when the
    /// most recent compile succeeded, or <c>null</c> if no factory call has
    /// happened yet. Thread-local.
    /// </summary>
    [System.ThreadStatic]
    private static string? t_lastRejection;

    public static string? LastRejection => t_lastRejection;

    internal static void RecordRejection(string? reason) => t_lastRejection = reason;

    /// <summary>Diagnostic: describe which StructuredLogger fields reflection can see.</summary>
    public static string DescribeFields()
    {
        var fields = typeof(StructuredLogger).GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        return string.Join(", ", System.Linq.Enumerable.Select(fields, f => f.Name));
    }

    /// <summary>Diagnostic: describe the pipeline wrapped by a StructuredLogger.</summary>
    public static string DescribePipeline(StructuredLogger logger)
    {
        System.ArgumentNullException.ThrowIfNull(logger);
        var pipelineField = typeof(StructuredLogger).GetField("_pipeline", BindingFlags.NonPublic | BindingFlags.Instance);
        var pipeline = pipelineField?.GetValue(logger);
        return pipeline?.GetType().Name ?? "null";
    }
}
