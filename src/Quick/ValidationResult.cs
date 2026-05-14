#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace MMP.Herald.Quick;

/// <summary>
/// Result of builder validation. Contains zero or more issues discovered
/// during pre-build checks.
/// </summary>
public sealed record ValidationResult(IReadOnlyList<ValidationIssue> Issues)
{
    /// <summary>True when no issues were found.</summary>
    public bool IsValid => Issues.Count == 0;

    /// <summary>True when at least one critical issue was found.</summary>
    public bool HasCritical => Issues.Any(static i => i.Severity == ValidationSeverity.Critical);
}

/// <summary>
/// A single validation issue found during builder validation.
/// </summary>
public sealed record ValidationIssue(string Message, ValidationSeverity Severity);

/// <summary>
/// Severity of a validation issue.
/// This is an enum rather than a sealed record because severity is a closed,
/// fixed set of values with no associated data. An enum gives zero heap
/// allocation, switch-friendly comparison, and clearer intent. Most domain
/// types in Herald use sealed records for extensibility and rich equality,
/// but severity is the exception: it's a pure discriminator with exactly
/// two values that will never carry payload or vary at runtime.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>Non-blocking issue that may cause unexpected behavior.</summary>
    Warning,

    /// <summary>Blocking issue that prevents the pipeline from working correctly.</summary>
    Critical
}
