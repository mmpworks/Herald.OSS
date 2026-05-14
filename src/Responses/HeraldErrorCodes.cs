#nullable enable

namespace MMP.Herald.Responses;

/// <summary>
/// Numeric error codes for every exception Herald can produce.
/// Used by TupleResponse to give callers a machine-friendly status.
///
/// Ranges:
///   0         = Success
///   1000-1099 = Argument validation (null, out of range, invalid)
///   1100-1199 = Configuration and bootstrap
///   1200-1299 = Level registry and level switch
///   1300-1399 = Pipeline runtime (async, batching, durable buffer)
///   1400-1499 = Resilience (circuit breaker, fallback, audit)
///   1500-1599 = File and IO (rolling files, WAL, hot reload)
///   1600-1699 = Output and rendering
///   1700-1799 = Quick builder
///   1800-1899 = Testing
///   9000-9099 = Unknown / unclassified
/// </summary>
public static class HeraldErrorCodes
{
    // -- Success --

    public const int Ok = 0;

    // -- 1000: Argument validation --

    /// <summary>A required parameter was null.</summary>
    public const int ArgumentNull = 1000;

    /// <summary>A numeric parameter was out of its valid range.</summary>
    public const int ArgumentOutOfRange = 1001;

    /// <summary>A string parameter was null, empty, or whitespace.</summary>
    public const int ArgumentInvalid = 1002;

    /// <summary>Duplicate key or name passed where uniqueness is required.</summary>
    public const int ArgumentDuplicate = 1003;

    // -- 1100: Configuration and bootstrap --

    /// <summary>JSON config deserialization failed.</summary>
    public const int ConfigDeserializationFailed = 1100;

    /// <summary>Required configuration section is missing or incomplete.</summary>
    public const int ConfigMissing = 1101;

    /// <summary>Configuration mapper found an invalid value (e.g., negative retry delay).</summary>
    public const int ConfigMapperInvalid = 1102;

    /// <summary>Configuration validation found critical issues during Build().</summary>
    public const int ConfigValidationFailed = 1103;

    /// <summary>Runtime configuration mapper could not resolve a reference.</summary>
    public const int ConfigReferenceNotFound = 1104;

    /// <summary>PostFiltering configuration missing required fields.</summary>
    public const int ConfigPostFilteringInvalid = 1105;

    // -- 1200: Level registry --

    /// <summary>Log level key not found in the registry.</summary>
    public const int LevelKeyNotFound = 1200;

    /// <summary>Duplicate log level key during registration.</summary>
    public const int LevelKeyDuplicate = 1201;

    /// <summary>Log level key is null or whitespace during registration.</summary>
    public const int LevelKeyInvalid = 1202;

    /// <summary>Level placement position not supported.</summary>
    public const int LevelPlacementUnsupported = 1203;

    /// <summary>Level already registered at a placement position.</summary>
    public const int LevelPlacementDuplicate = 1204;

    // -- 1300: Pipeline runtime --

    /// <summary>AsyncLogger capacity was non-positive.</summary>
    public const int AsyncCapacityInvalid = 1300;

    /// <summary>BatchingLogger received a null next logger.</summary>
    public const int BatchingNextNull = 1301;

    /// <summary>DurableBufferLogger inner sink failure (event persisted in WAL).</summary>
    public const int DurableBufferDeliveryFailed = 1302;

    /// <summary>EventProcessingLogger received null processors.</summary>
    public const int EventProcessorNull = 1303;

    /// <summary>Pipeline processor chain validation failed.</summary>
    public const int PipelineValidationFailed = 1304;

    /// <summary>Pipeline accessor error codes.</summary>
    public static class Pipeline
    {
        /// <summary>Requested pipeline component was not registered.</summary>
        public const int ComponentNotFound = 1310;
    }

    // -- 1400: Resilience --

    /// <summary>Circuit breaker is open - event rejected.</summary>
    public const int CircuitBreakerOpen = 1400;

    /// <summary>Circuit breaker probe failed - reverted to open state.</summary>
    public const int CircuitBreakerProbeFailed = 1401;

    /// <summary>Circuit breaker failure threshold was non-positive.</summary>
    public const int CircuitBreakerThresholdInvalid = 1402;

    /// <summary>Circuit breaker recovery period was non-positive.</summary>
    public const int CircuitBreakerRecoveryInvalid = 1403;

    /// <summary>Audit sink failed to deliver a compliance-critical event.</summary>
    public const int AuditDeliveryFailed = 1404;

    /// <summary>Fallback sink also failed after primary failure.</summary>
    public const int FallbackAlsoFailed = 1405;

    // -- 1500: File and IO --

    /// <summary>Configuration file not found during hot reload switch.</summary>
    public const int ConfigFileNotFound = 1500;

    /// <summary>Hot reload rebuild did not produce a SwappableLogger.</summary>
    public const int HotReloadSwapFailed = 1501;

    /// <summary>Hot reload config file read or parse failed.</summary>
    public const int HotReloadParseFailed = 1502;

    /// <summary>Cannot determine directory from file path.</summary>
    public const int FileDirectoryInvalid = 1503;

    /// <summary>Single log message exceeds the rolling file's MaxBytes limit.</summary>
    public const int RollingFileMessageTooLarge = 1504;

    /// <summary>WAL directory could not be created or accessed.</summary>
    public const int WalDirectoryFailed = 1505;

    // -- 1600: Output and rendering --

    /// <summary>Output transformer kind not supported.</summary>
    public const int TransformerKindUnsupported = 1600;

    /// <summary>Output alias not registered in transformer registry.</summary>
    public const int TransformerAliasNotFound = 1601;

    /// <summary>Base alias not found when building transformer chain.</summary>
    public const int TransformerBaseAliasNotFound = 1602;

    // -- 1700: Quick builder --

    /// <summary>FastLog/FastLogAsync called without a file sink configured.</summary>
    public const int QuickBuilderNoFileSink = 1700;

    /// <summary>Build() validation found critical issues.</summary>
    public const int QuickBuilderBuildFailed = 1701;

    // -- 1800: Testing --

    /// <summary>Log assertion failed (test harness).</summary>
    public const int LogAssertionFailed = 1800;

    // -- 5000: User-defined --
    // Range 5000-8999 is reserved for application-specific error codes.
    // Register custom exception types via HeraldExceptionMap.Register().

    /// <summary>Base code for user-defined error codes (5000-8999).</summary>
    public const int UserDefined = 5000;

    // -- 9000: Unknown --

    /// <summary>Unclassified or unexpected exception.</summary>
    public const int Unknown = 9000;

    /// <summary>An exception occurred but its type was not recognized by the error code registry.</summary>
    public const int Unrecognized = 9001;
}
