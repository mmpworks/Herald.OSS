#nullable enable

using System.Diagnostics.CodeAnalysis;

namespace MMP.Herald.Adapters;

/// <summary>
/// Engine-side capture-time redaction hook. Defined in the engine namespace
/// (<c>MMP.Herald.Adapters</c>) so the shared <see cref="HeraldAdapterCore"/> can
/// apply redaction WITHOUT referencing any Serilog mirror type — the
/// Guard1 confinement invariant (no engine type may reference an
/// <c>MMP.Herald.Serilog.*</c> type) stays intact.
///
/// <para>
/// The Serilog destructuring applicator (a mirror type) implements this interface;
/// the engine sees only the interface. NLog and log4net have no destructuring
/// grammar and pass <c>null</c> — the seam stays open so a future redaction route
/// on any skin reaches the same engine path.
/// </para>
/// </summary>
internal interface ICaptureRedactor
{
    /// <summary>Whether any redaction policies are registered (cheap pre-check).</summary>
    bool HasPolicies { get; }

    /// <summary>
    /// Run the policy chain against <paramref name="value"/>. When a policy claims
    /// it, return a native-renderable redacted value in <paramref name="redacted"/>
    /// and <c>true</c>; otherwise <c>false</c> and the caller keeps the original.
    /// </summary>
    [RequiresUnreferencedCode(
        "Redaction policies may walk arbitrary object graphs via reflection.")]
    bool TryRedactNative(object? value, out object? redacted);
}
