#nullable enable

using MMP.Herald.Services;

namespace MMP.Herald.Addons.Compliance;

/// <summary>
/// Context keys used by compliance addons.
/// Delegates to LogContextKeys - the single source of truth.
/// </summary>
public static class ComplianceContextKeys
{
    /// <summary>HMAC-SHA256 chain hash (set by HmacChainLogger).</summary>
    public const string HmacChain = LogContextKeys.HmacChain;

    /// <summary>Monotonic sequence number (set by SequenceNumberEnricher).</summary>
    public const string SequenceNumber = LogContextKeys.SequenceNumber;
}
