#nullable enable

// Layer-2 mirror of Serilog.Core.ILogEventPropertyValueFactory.
// In real Serilog this interface is in Serilog.Core.
// Layer-1 twin: MMP.Herald.Serilog.Events.ILogEventPropertyValueFactory
//
// Used by IDestructuringPolicy: the policy receives this factory to construct
// child value nodes when walking object graphs.

using System.Diagnostics.CodeAnalysis;

namespace Serilog.Core;

/// <summary>
/// Factory for constructing <see cref="Events.LogEventPropertyValue"/> nodes.
/// Exported under <c>Serilog.Core</c> to match the real Serilog namespace.
/// </summary>
public interface ILogEventPropertyValueFactory
{
    /// <summary>Convert <paramref name="value"/> to a property value node.</summary>
    [RequiresUnreferencedCode(
        "Value projection uses reflection on arbitrary user types.")]
    Events.LogEventPropertyValue CreatePropertyValue(object? value, bool destructureObjects);
}
