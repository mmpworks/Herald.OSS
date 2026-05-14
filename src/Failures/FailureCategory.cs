#nullable enable

namespace MMP.Herald.Failures;

/// <summary>
/// Classification of a sink failure for smarter circuit breaker recovery.
/// </summary>
public sealed record FailureCategory(string Value)
{
    /// <summary>Temporary issue that may resolve on retry (timeout, 5xx, socket reset).</summary>
    public static FailureCategory Transient { get; } = new("Transient");

    /// <summary>Permanent issue that will not resolve on retry (4xx, auth failure, bad config).</summary>
    public static FailureCategory Permanent { get; } = new("Permanent");

    /// <summary>Unclassified exception - treated as transient for safety.</summary>
    public static FailureCategory Unknown { get; } = new("Unknown");

    public override string ToString() => Value;
}
