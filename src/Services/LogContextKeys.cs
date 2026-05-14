#nullable enable

namespace MMP.Herald.Services;

/// <summary>
/// Well-known context key names used by Herald's built-in components.
/// Use these instead of raw strings when setting or checking context values.
/// </summary>
public static class LogContextKeys
{
    /// <summary>Channel name for routing events to channel sinks.</summary>
    public const string Channel = "channel";

    /// <summary>Audit flag that bypasses level filtering.</summary>
    public const string Audit = "audit";

    /// <summary>Exception context key for exception enrichment.</summary>
    public const string Exception = "exception";

    /// <summary>W3C trace ID from System.Diagnostics.Activity.</summary>
    public const string TraceId = "traceId";

    /// <summary>W3C span ID from System.Diagnostics.Activity.</summary>
    public const string SpanId = "spanId";

    /// <summary>Deferred rendering sentinel (set by DeferredLogEventFactory).</summary>
    public const string DeferredSentinel = "_herald:deferred";

    /// <summary>HMAC chain hash (set by HmacChainLogger).</summary>
    public const string HmacChain = "_hmacChain";

    /// <summary>Duplicate event count (set by LogDeduplicationProcessor).</summary>
    public const string DuplicateCount = "_duplicateCount";

    /// <summary>Replay marker (set by DurableBufferLogger and LogReplayReader).</summary>
    public const string Replayed = "_replayed";

    /// <summary>Original sequence number from WAL replay.</summary>
    public const string ReplaySequence = "_originalSequence";

    /// <summary>Sequence number (set by SequenceNumberEnricher).</summary>
    public const string SequenceNumber = "sequenceNumber";

    /// <summary>Correlation ID for cross-cutting request/operation tracking (set by CorrelationIdEnricher).</summary>
    public const string CorrelationId = "correlationId";

    // -- Service identity context keys (set by ServiceIdentityEnricher) --

    /// <summary>Service name (set by ServiceIdentityEnricher).</summary>
    public const string ServiceName = "service.name";

    /// <summary>Service version (set by ServiceIdentityEnricher).</summary>
    public const string ServiceVersion = "service.version";

    /// <summary>Deployment region or environment (set by ServiceIdentityEnricher).</summary>
    public const string ServiceRegion = "service.region";

    /// <summary>Deployment environment - e.g., "production", "staging" (set by ServiceIdentityEnricher).</summary>
    public const string ServiceEnvironment = "service.environment";

    // -- Enricher context keys (set by core enrichers) --

    /// <summary>Machine name (set by MachineNameLogEnricher).</summary>
    public const string MachineName = "machineName";

    /// <summary>Process ID (set by ProcessIdLogEnricher).</summary>
    public const string ProcessId = "processId";

    /// <summary>Process name (set by ProcessIdLogEnricher).</summary>
    public const string ProcessName = "processName";

    /// <summary>Managed thread ID (set by ThreadIdLogEnricher).</summary>
    public const string ThreadId = "threadId";

    /// <summary>Whether the thread is from the thread pool (set by ThreadIdLogEnricher).</summary>
    public const string IsThreadPoolThread = "isThreadPoolThread";

    // -- Caller info context keys (set by StructuredLogger) --

    /// <summary>Caller member name.</summary>
    public const string CallerMember = "callerMember";

    /// <summary>Caller source file name.</summary>
    public const string CallerFile = "callerFile";

    /// <summary>Caller source line number.</summary>
    public const string CallerLine = "callerLine";
}
