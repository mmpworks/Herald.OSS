#nullable enable

// Layer-2 mirror of Serilog.Core.ILogEventPropertyFactory.
// In real Serilog this interface lives in Serilog.Core; the Layer-1 twin
// lives in MMP.Herald.Serilog.Events. We declare it here under Serilog.Core
// to match the real Serilog namespace so consumer enricher code compiles unchanged.
//
// Layer-1 twin: MMP.Herald.Serilog.Events.ILogEventPropertyFactory

using System.Diagnostics.CodeAnalysis;

namespace Serilog.Core;

/// <summary>
/// Factory for constructing named Serilog-shaped properties.
/// Exported under the <c>Serilog.Core</c> namespace to match the real Serilog API.
/// Consumer enrichers reference this type in their <c>Enrich</c> method signature.
/// </summary>
public interface ILogEventPropertyFactory
{
    /// <summary>
    /// Create a named <see cref="Events.LogEventProperty"/> from a raw value.
    /// </summary>
    [RequiresUnreferencedCode(
        "Value projection uses reflection on arbitrary user types.")]
    Events.LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false);
}
