#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using MMP.Herald.Failures;
using MMP.Herald.Pipeline;
using MMP.Herald.Testing;

namespace MMP.Herald.Responses;

/// <summary>
/// Static facade over the global default <see cref="ExceptionRegistry"/>.
/// Maps exception types to numeric error codes.
///
/// For per-pipeline isolation, create your own <see cref="ExceptionRegistry"/>
/// instance. This static class delegates to <see cref="Default"/>.
///
/// Usage:
///   // Global (static) - shared across the process
///   HeraldExceptionMap.Register&lt;QuestNotFoundException&gt;(5000, "QuestNotFound", "Gameplay", "...");
///   int code = HeraldExceptionMap.GetCode(ex);
///
///   // Per-pipeline (instance) - isolated
///   var registry = ExceptionRegistry.CreateWithDefaults();
///   registry.Register&lt;QuestNotFoundException&gt;(5000, "QuestNotFound", "Gameplay", "...");
///   int code = registry.GetCode(ex);
/// </summary>
public static class HeraldExceptionMap
{
    /// <summary>
    /// Describes one entry in the exception manifest.
    /// </summary>
    public sealed record ManifestEntry(
        int Code,
        string Name,
        string ExceptionType,
        string Category,
        string Description);

    /// <summary>
    /// The global default registry, shared across the process.
    /// All static methods on this class delegate here.
    /// </summary>
    public static ExceptionRegistry Default { get; } = CreateDefaultRegistry();

    // -- Public API: delegates to Default --

    /// <summary>
    /// Resolve the best error code for a given exception.
    /// </summary>
    public static int GetCode(Exception ex) => Default.GetCode(ex);

    /// <summary>Get the human-readable name for an error code, or null.</summary>
    public static string? GetName(int code) => Default.GetName(code);

    /// <summary>Get the full manifest entry for an error code, or null.</summary>
    public static ManifestEntry? GetEntry(int code) => Default.GetEntry(code);

    /// <summary>
    /// The complete exception manifest, including user-registered codes.
    /// </summary>
    public static IReadOnlyDictionary<int, ManifestEntry> Manifest => Default.Manifest;

    /// <summary>All manifest entries sorted by code.</summary>
    public static IReadOnlyList<ManifestEntry> GetAllEntries() => Default.GetAllEntries();

    // -- Registration: delegates to Default --

    /// <summary>
    /// Register a custom exception type with the global registry.
    /// For per-pipeline isolation, use an <see cref="ExceptionRegistry"/> instance instead.
    /// </summary>
    public static void Register<TException>(
        int code, string name, string category, string description)
        where TException : Exception =>
        Default.Register<TException>(code, name, category, description);

    /// <summary>Register using a pre-built ManifestEntry.</summary>
    public static void Register<TException>(ManifestEntry entry)
        where TException : Exception =>
        Default.Register<TException>(entry);

    /// <summary>Update an existing entry's metadata.</summary>
    public static bool Update(int code, string name, string category, string description) =>
        Default.Update(code, name, category, description);

    /// <summary>Remove by code (built-in codes below 5000 are protected).</summary>
    public static bool Remove(int code) => Default.Remove(code);

    /// <summary>Remove by exception type (built-in codes are protected).</summary>
    public static bool Remove<TException>() where TException : Exception =>
        Default.Remove<TException>();

    /// <summary>Check whether an exception type is registered.</summary>
    public static bool Has<TException>() where TException : Exception =>
        Default.Has<TException>();

    /// <summary>Check whether an error code is registered.</summary>
    public static bool Has(int code) => Default.Has(code);

    // -- Internal helpers used by ExceptionRegistry --

    /// <summary>
    /// Populate dictionaries with Herald's built-in exception codes.
    /// Called by ExceptionRegistry.CreateWithDefaults().
    /// </summary>
    internal static void PopulateDefaults(
        Dictionary<Type, int> typeToCode,
        Dictionary<int, ManifestEntry> codeToEntry)
    {
        var entries = BuildManifest();

        foreach (var entry in entries)
        {
            codeToEntry[entry.Code] = entry;

            var type = entry.ExceptionType switch
            {
                ExceptionTypes.AuditLogFailure => typeof(AuditLogFailureException),
                ExceptionTypes.CircuitBreakerOpen => typeof(CircuitBreakerOpenException),
                ExceptionTypes.LogAssertion => typeof(LogAssertionException),
                _ => null
            };

            if (type is not null && !typeToCode.ContainsKey(type))
            {
                typeToCode[type] = entry.Code;
            }
        }
    }

    /// <summary>
    /// Classify a BCL exception using message heuristics.
    /// Called by ExceptionRegistry.GetCode() as a fallback when no type match is found.
    /// </summary>
    internal static int ClassifyBclException(Exception ex) =>
        ex switch
        {
            ArgumentNullException => HeraldErrorCodes.ArgumentNull,
            ArgumentOutOfRangeException => HeraldErrorCodes.ArgumentOutOfRange,
            ArgumentException => HeraldErrorCodes.ArgumentInvalid,
            FileNotFoundException => HeraldErrorCodes.ConfigFileNotFound,
            KeyNotFoundException e => ClassifyKeyNotFound(e),
            ObjectDisposedException => HeraldErrorCodes.Unknown,
            InvalidOperationException e => ClassifyInvalidOperation(e),
            IOException => HeraldErrorCodes.WalDirectoryFailed,
            _ => HeraldErrorCodes.Unrecognized
        };

    // -- Private implementation --

    private static ExceptionRegistry CreateDefaultRegistry()
    {
        var registry = ExceptionRegistry.CreateWithDefaults();
        return registry;
    }

    // -- Message heuristics for BCL types --

    // Cognitive complexity note: these switches classify a single BCL type
    // by scanning the message for Herald-specific keywords. Each case maps
    // to exactly one error code. The structure is flat - no nesting.

    private static readonly (string Keyword, int Code)[] KeyNotFoundRules =
    [
        ("level", HeraldErrorCodes.LevelKeyNotFound),
        ("alias", HeraldErrorCodes.TransformerAliasNotFound),
        ("transformer", HeraldErrorCodes.TransformerAliasNotFound),
    ];

    private static readonly (string Keyword, int Code)[] InvalidOperationRules =
    [
        ("circuit breaker", HeraldErrorCodes.CircuitBreakerOpen),
        ("duplicate", HeraldErrorCodes.LevelKeyDuplicate),
        ("hot reload", HeraldErrorCodes.HotReloadSwapFailed),
        ("SwappableLogger", HeraldErrorCodes.HotReloadSwapFailed),
        ("validation", HeraldErrorCodes.ConfigValidationFailed),
        ("critical", HeraldErrorCodes.ConfigValidationFailed), // keyword match on exception message text — not a Herald level key
        ("file sink", HeraldErrorCodes.QuickBuilderNoFileSink),
        ("FastLog", HeraldErrorCodes.QuickBuilderNoFileSink),
        ("MaxBytes", HeraldErrorCodes.RollingFileMessageTooLarge),
        ("exceeds", HeraldErrorCodes.RollingFileMessageTooLarge),
        ("directory", HeraldErrorCodes.FileDirectoryInvalid),
        ("placement", HeraldErrorCodes.LevelPlacementUnsupported),
        ("transformer", HeraldErrorCodes.TransformerKindUnsupported),
        ("processor", HeraldErrorCodes.PipelineValidationFailed),
        ("pipeline", HeraldErrorCodes.PipelineValidationFailed),
        ("PostFiltering", HeraldErrorCodes.ConfigPostFilteringInvalid),
    ];

    private static int ClassifyKeyNotFound(KeyNotFoundException ex) =>
        ClassifyByKeyword(ex.Message, KeyNotFoundRules, HeraldErrorCodes.ConfigReferenceNotFound);

    private static int ClassifyInvalidOperation(InvalidOperationException ex) =>
        ClassifyByKeyword(ex.Message, InvalidOperationRules, HeraldErrorCodes.Unknown);

    private static int ClassifyByKeyword(
        string message, (string Keyword, int Code)[] rules, int fallback)
    {
        foreach (var (keyword, code) in rules)
        {
            if (message.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return code;
        }
        return fallback;
    }

    // -- Manifest builder --

    private static List<ManifestEntry> BuildManifest() =>
    [
        new(HeraldErrorCodes.Ok,
            "Ok", "-", "Success",
            "Operation completed successfully."),

        // 1000: Argument validation
        new(HeraldErrorCodes.ArgumentNull,
            "ArgumentNull", ExceptionTypes.ArgumentNull, "Validation",
            "A required parameter was null."),
        new(HeraldErrorCodes.ArgumentOutOfRange,
            "ArgumentOutOfRange", ExceptionTypes.ArgumentOutOfRange, "Validation",
            "A numeric parameter was outside its valid range (e.g., capacity <= 0, debounceMs <= 0)."),
        new(HeraldErrorCodes.ArgumentInvalid,
            "ArgumentInvalid", ExceptionTypes.Argument, "Validation",
            "A string parameter was null, empty, or whitespace where a value is required."),
        new(HeraldErrorCodes.ArgumentDuplicate,
            "ArgumentDuplicate", ExceptionTypes.Argument, "Validation",
            "A duplicate key or name was passed where uniqueness is required."),

        // 1100: Configuration
        new(HeraldErrorCodes.ConfigDeserializationFailed,
            "ConfigDeserializationFailed", ExceptionTypes.InvalidOperation, "Configuration",
            "JSON configuration string could not be deserialized."),
        new(HeraldErrorCodes.ConfigMissing,
            "ConfigMissing", ExceptionTypes.InvalidOperation, "Configuration",
            "A required configuration section is missing or incomplete."),
        new(HeraldErrorCodes.ConfigMapperInvalid,
            "ConfigMapperInvalid", ExceptionTypes.InvalidOperation, "Configuration",
            "Configuration mapper found an invalid value (e.g., MaxAttempts <= 0, DelayMs < 0)."),
        new(HeraldErrorCodes.ConfigValidationFailed,
            "ConfigValidationFailed", ExceptionTypes.InvalidOperation, "Configuration",
            "Build() validation found critical issues preventing pipeline assembly."),
        new(HeraldErrorCodes.ConfigReferenceNotFound,
            "ConfigReferenceNotFound", ExceptionTypes.KeyNotFound, "Configuration",
            "A configuration reference (level key, category override) could not be resolved."),
        new(HeraldErrorCodes.ConfigPostFilteringInvalid,
            "ConfigPostFilteringInvalid", ExceptionTypes.InvalidOperation, "Configuration",
            "PostFiltering configuration is missing required fields."),

        // 1200: Level registry
        new(HeraldErrorCodes.LevelKeyNotFound,
            "LevelKeyNotFound", ExceptionTypes.KeyNotFound, "LevelRegistry",
            "A log level key was not found in the registry."),
        new(HeraldErrorCodes.LevelKeyDuplicate,
            "LevelKeyDuplicate", ExceptionTypes.InvalidOperation, "LevelRegistry",
            "A log level key is already registered."),
        new(HeraldErrorCodes.LevelKeyInvalid,
            "LevelKeyInvalid", ExceptionTypes.Argument, "LevelRegistry",
            "Log level key is null or whitespace during registration."),
        new(HeraldErrorCodes.LevelPlacementUnsupported,
            "LevelPlacementUnsupported", ExceptionTypes.InvalidOperation, "LevelRegistry",
            "Level placement position is not supported by the registry."),
        new(HeraldErrorCodes.LevelPlacementDuplicate,
            "LevelPlacementDuplicate", ExceptionTypes.InvalidOperation, "LevelRegistry",
            "A level is already registered at the requested placement position."),

        // 1300: Pipeline
        new(HeraldErrorCodes.AsyncCapacityInvalid,
            "AsyncCapacityInvalid", ExceptionTypes.ArgumentOutOfRange, "Pipeline",
            "AsyncLogger capacity must be greater than zero."),
        new(HeraldErrorCodes.BatchingNextNull,
            "BatchingNextNull", ExceptionTypes.ArgumentNull, "Pipeline",
            "BatchingLogger requires a non-null next logger in the chain."),
        new(HeraldErrorCodes.DurableBufferDeliveryFailed,
            "DurableBufferDeliveryFailed", ExceptionTypes.InvalidOperation, "Pipeline",
            "DurableBufferLogger inner sink failed. Event persisted in WAL for retry."),
        new(HeraldErrorCodes.EventProcessorNull,
            "EventProcessorNull", ExceptionTypes.ArgumentNull, "Pipeline",
            "EventProcessingLogger requires a non-null processor list."),
        new(HeraldErrorCodes.PipelineValidationFailed,
            "PipelineValidationFailed", ExceptionTypes.InvalidOperation, "Pipeline",
            "Pipeline processor chain validation failed."),

        // 1400: Resilience
        new(HeraldErrorCodes.CircuitBreakerOpen,
            "CircuitBreakerOpen", ExceptionTypes.CircuitBreakerOpen, "Resilience",
            "Circuit breaker is in Open state. Events are rejected until recovery."),
        new(HeraldErrorCodes.CircuitBreakerProbeFailed,
            "CircuitBreakerProbeFailed", ExceptionTypes.CircuitBreakerOpen, "Resilience",
            "Circuit breaker probe attempt failed. Reverted to Open state."),
        new(HeraldErrorCodes.CircuitBreakerThresholdInvalid,
            "CircuitBreakerThresholdInvalid", ExceptionTypes.ArgumentOutOfRange, "Resilience",
            "Circuit breaker failure threshold must be greater than zero."),
        new(HeraldErrorCodes.CircuitBreakerRecoveryInvalid,
            "CircuitBreakerRecoveryInvalid", ExceptionTypes.ArgumentOutOfRange, "Resilience",
            "Circuit breaker recovery period must be greater than TimeSpan.Zero."),
        new(HeraldErrorCodes.AuditDeliveryFailed,
            "AuditDeliveryFailed", ExceptionTypes.AuditLogFailure, "Resilience",
            "Audit-mode sink failed to deliver a compliance-critical event."),
        new(HeraldErrorCodes.FallbackAlsoFailed,
            "FallbackAlsoFailed", ExceptionTypes.AuditLogFailure, "Resilience",
            "Both primary and fallback sinks failed for an audit event."),

        // 1500: File and IO
        new(HeraldErrorCodes.ConfigFileNotFound,
            "ConfigFileNotFound", ExceptionTypes.FileNotFound, "IO",
            "Configuration file not found during SwitchConfigFile()."),
        new(HeraldErrorCodes.HotReloadSwapFailed,
            "HotReloadSwapFailed", ExceptionTypes.InvalidOperation, "IO",
            "Hot reload rebuild did not produce a SwappableLogger."),
        new(HeraldErrorCodes.HotReloadParseFailed,
            "HotReloadParseFailed", ExceptionTypes.InvalidOperation, "IO",
            "Hot reload config file could not be read or parsed."),
        new(HeraldErrorCodes.FileDirectoryInvalid,
            "FileDirectoryInvalid", ExceptionTypes.InvalidOperation, "IO",
            "Cannot determine directory from the given file path."),
        new(HeraldErrorCodes.RollingFileMessageTooLarge,
            "RollingFileMessageTooLarge", ExceptionTypes.InvalidOperation, "IO",
            "A single log message exceeds the rolling file's MaxBytes limit."),
        new(HeraldErrorCodes.WalDirectoryFailed,
            "WalDirectoryFailed", ExceptionTypes.IO, "IO",
            "WAL directory could not be created or accessed."),

        // 1600: Output
        new(HeraldErrorCodes.TransformerKindUnsupported,
            "TransformerKindUnsupported", ExceptionTypes.InvalidOperation, "Output",
            "The requested output transformer kind is not supported."),
        new(HeraldErrorCodes.TransformerAliasNotFound,
            "TransformerAliasNotFound", ExceptionTypes.KeyNotFound, "Output",
            "Output alias not registered in the transformer registry."),
        new(HeraldErrorCodes.TransformerBaseAliasNotFound,
            "TransformerBaseAliasNotFound", ExceptionTypes.KeyNotFound, "Output",
            "Base alias not found when building a transformer chain."),

        // 1700: Quick builder
        new(HeraldErrorCodes.QuickBuilderNoFileSink,
            "QuickBuilderNoFileSink", ExceptionTypes.InvalidOperation, "QuickBuilder",
            "FastLog/FastLogAsync requires a file sink to be configured."),
        new(HeraldErrorCodes.QuickBuilderBuildFailed,
            "QuickBuilderBuildFailed", ExceptionTypes.InvalidOperation, "QuickBuilder",
            "Build() failed due to critical validation issues."),

        // 1800: Testing
        new(HeraldErrorCodes.LogAssertionFailed,
            "LogAssertionFailed", ExceptionTypes.LogAssertion, "Testing",
            "A log assertion failed in the test harness."),

        // 9000: Unknown
        new(HeraldErrorCodes.Unknown,
            "Unknown", "-", "Unknown",
            "An unclassified or unexpected error occurred."),
        new(HeraldErrorCodes.Unrecognized,
            "Unrecognized", "-", "Unknown",
            "An exception occurred whose type is not in the Herald error code registry."),
    ];
}
