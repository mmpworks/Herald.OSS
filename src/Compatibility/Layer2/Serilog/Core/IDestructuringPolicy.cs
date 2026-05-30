#nullable enable

// Layer-2 mirror of Serilog.Core.IDestructuringPolicy.
// Uses Serilog.Core.ILogEventPropertyValueFactory (L2) and
// Serilog.Events.LogEventPropertyValue (L2) in the method signature.
//
// Layer-1 twin: MMP.Herald.Serilog.Core.IDestructuringPolicy

using System.Diagnostics.CodeAnalysis;

namespace Serilog.Core;

/// <summary>
/// Serilog-compatible destructuring policy exported under the <c>Serilog</c> assembly identity.
/// A consumer's <c>class MyPolicy : Serilog.Core.IDestructuringPolicy</c> compiles unchanged
/// after swapping the Serilog package for this assembly.
/// </summary>
public interface IDestructuringPolicy
{
    /// <summary>
    /// Attempt to destructure <paramref name="value"/> into a
    /// <see cref="Events.LogEventPropertyValue"/> tree node.
    /// </summary>
    /// <returns>
    /// <c>true</c> if this policy handled <paramref name="value"/>
    /// and <paramref name="result"/> is set; <c>false</c> to pass to the next policy.
    /// </returns>
    [RequiresUnreferencedCode(
        "Destructuring policies may walk arbitrary object graphs via reflection.")]
    bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        out Events.LogEventPropertyValue result);
}
