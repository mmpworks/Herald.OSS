#nullable enable

namespace MMP.Herald.Output.Writers;

/// <summary>
/// Resolves log file paths before use.
/// The default implementation passes paths through unchanged.
/// Host-specific implementations can resolve virtual paths
/// (e.g., engine-specific virtual path prefixes).
/// </summary>
public interface ILogFilePathResolver
{
    string Resolve(string filePath);
}
