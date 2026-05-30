#nullable enable

using System.Diagnostics.CodeAnalysis;

namespace MMP.Herald.Serilog.Events;

/// <summary>
/// Public factory interface for constructing named Serilog-shaped properties.
/// Must be public: Serilog enricher signatures reference this interface.
/// </summary>
public interface ILogEventPropertyFactory
{
    /// <summary>
    /// Create a named LogEventProperty from a raw value.
    /// </summary>
    [RequiresUnreferencedCode(
        "Value projection uses reflection on arbitrary user types.")]
    LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false);
}
