#nullable enable

using System.Diagnostics.CodeAnalysis;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Core;

/// <summary>
/// Serilog-compatible destructuring policy.  A consumer implements this
/// interface to intercept the {@Name} capture path and substitute a custom
/// <see cref="LogEventPropertyValue"/> tree in place of the default
/// reflection walk.
///
/// <para>
/// <strong>Security contract.</strong>  The primary use-case for this interface
/// is redaction: returning a <see cref="StructureValue"/> that carries only
/// approved fields strips secrets before they reach any sink or formatter.
/// A no-op'd policy (one that always returns <c>false</c>, or one whose
/// output is silently discarded by the bridge) is a PII leak.  The bridge in
/// <c>SerilogDestructuringPolicyBridge</c> is designed so that if it cannot
/// call this interface, it throws loudly at registration rather than silently
/// degrading.
/// </para>
///
/// <para>
/// Usage: register via <c>new LoggerConfiguration().Destructure.With(myPolicy)</c>.
/// </para>
/// </summary>
public interface IDestructuringPolicy
{
    /// <summary>
    /// Attempt to destructure <paramref name="value"/> into a
    /// <see cref="LogEventPropertyValue"/> tree node.
    /// </summary>
    /// <param name="value">The value to destructure. Never null when called
    /// through the Herald bridge (null is short-circuited upstream).</param>
    /// <param name="propertyValueFactory">
    /// Factory for constructing child value nodes.  Pass sub-values through
    /// this factory to respect the full policy chain for nested objects.
    /// </param>
    /// <param name="result">
    /// The substituted value node when the method returns <c>true</c>.
    /// Ignored when the method returns <c>false</c>.
    /// </param>
    /// <returns>
    /// <c>true</c> if this policy handled <paramref name="value"/> and
    /// <paramref name="result"/> is set.  <c>false</c> to pass to the
    /// next policy in the chain.
    /// </returns>
    [RequiresUnreferencedCode(
        "Destructuring policies may walk arbitrary object graphs via reflection.")]
    bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        out LogEventPropertyValue result);
}
