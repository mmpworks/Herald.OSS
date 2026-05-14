#nullable enable

namespace MMP.Herald.Services;

/// <summary>
/// Well-known drop strategy names for AsyncLogger queue overflow behavior.
/// </summary>
public static class KnownDropStrategies
{
    /// <summary>Drop the new event when the queue is full (default). Non-blocking.</summary>
    public const string DropWrite = "drop_write";

    /// <summary>Drop the oldest event in the queue to make room. Non-blocking.</summary>
    public const string DropOldest = "drop_oldest";

    /// <summary>Block the caller until space is available. Natural backpressure.</summary>
    public const string Wait = "wait";
}
