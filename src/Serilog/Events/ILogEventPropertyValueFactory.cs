#nullable enable

using System.Diagnostics.CodeAnalysis;
using MMP.Herald.Serilog.Events;

// Mirrors real Serilog's Serilog.Core.ILogEventPropertyValueFactory position so a
// consumer's `using Serilog.Core;` -> `using MMP.Herald.Serilog.Core;` resolves it.
namespace MMP.Herald.Serilog.Core;

/// <summary>
/// Public factory interface for constructing Serilog-shaped value nodes.
/// Must be public: consumer enrichers name this in method signatures.
/// </summary>
public interface ILogEventPropertyValueFactory
{
    /// <summary>
    /// Convert value to a LogEventPropertyValue node.
    /// When destructureObjects is true, complex objects are walked reflectively.
    /// </summary>
    [RequiresUnreferencedCode(
        "Value projection uses reflection on arbitrary user types.")]
    LogEventPropertyValue CreatePropertyValue(object? value, bool destructureObjects);
}
