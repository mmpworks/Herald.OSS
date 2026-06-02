#nullable enable

using System.Diagnostics.CodeAnalysis;
using MMP.Herald.Serilog.Events;

// Mirrors real Serilog's Serilog.Core.ILogEventPropertyFactory position so a
// consumer's `using Serilog.Core;` -> `using MMP.Herald.Serilog.Core;` resolves it.
namespace MMP.Herald.Serilog.Core;

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
