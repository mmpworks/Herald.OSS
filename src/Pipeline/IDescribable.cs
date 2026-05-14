#nullable enable

namespace MMP.Herald.Pipeline;

/// <summary>
/// Pipeline components that can describe themselves for diagnostic output.
/// Used by DiagnosticDump() to print the active pipeline chain.
/// </summary>
public interface IDescribable
{
    /// <summary>
    /// Returns a short human-readable description for diagnostics.
    /// Example: "AsyncLogger(capacity:1024,drop:drop_write)"
    /// </summary>
    string Describe();
}
