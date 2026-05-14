#nullable enable

namespace MMP.Herald.Failures;

/// <summary>
/// Named constants for every exception type that Herald can throw or propagate
/// to application code. Use these in catch blocks, error mapping, and diagnostics
/// to avoid magic strings.
///
/// Organized by category:
/// - Validation: argument and parameter errors at public API boundaries
/// - Configuration: bootstrap, mapping, and registry errors during setup
/// - Pipeline: runtime errors from decorators and sinks
/// - Resilience: circuit breaker, fallback, and audit errors
/// - IO: file system and network transport errors
/// - Testing: assertion failures in test harness
/// </summary>
public static class ExceptionTypes
{
    // -- .NET BCL types thrown by Herald --

    public const string ArgumentNull = "ArgumentNullException";
    public const string ArgumentOutOfRange = "ArgumentOutOfRangeException";
    public const string Argument = "ArgumentException";
    public const string InvalidOperation = "InvalidOperationException";
    public const string KeyNotFound = "KeyNotFoundException";
    public const string FileNotFound = "FileNotFoundException";
    public const string IO = "IOException";
    public const string ObjectDisposed = "ObjectDisposedException";

    // -- Herald custom exceptions --

    public const string AuditLogFailure = "AuditLogFailureException";
    public const string CircuitBreakerOpen = "CircuitBreakerOpenException";
    public const string LogAssertion = "LogAssertionException";

    // -- Category groupings for filtering --

    /// <summary>All validation-related exception type names.</summary>
    public static readonly string[] Validation =
    [
        ArgumentNull,
        ArgumentOutOfRange,
        Argument
    ];

    /// <summary>All configuration-related exception type names.</summary>
    public static readonly string[] Configuration =
    [
        InvalidOperation,
        KeyNotFound
    ];

    /// <summary>All resilience-related exception type names.</summary>
    public static readonly string[] Resilience =
    [
        AuditLogFailure,
        CircuitBreakerOpen
    ];

    /// <summary>All IO-related exception type names.</summary>
    public static readonly string[] FileAndIO =
    [
        FileNotFound,
        IO,
        ObjectDisposed
    ];

    /// <summary>All testing-related exception type names.</summary>
    public static readonly string[] Testing =
    [
        LogAssertion
    ];
}
