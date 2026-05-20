#nullable enable

using System;

namespace MMP.Herald;

/// <summary>
/// SDK-boundary record naming a single capability a component requires
/// from the running edition.
///
/// <para>
/// The record carries an implicit conversion from <see cref="string"/> so
/// existing collection-expression call sites stay identical: a plugin
/// author writing <c>RequiredCapabilities =&gt; ["multi-tenant"]</c> gets
/// a <see cref="CapabilityRequirement"/> list via the conversion without
/// touching the source. The wire format stays a flat string array in v1;
/// the record exists so future per-cap metadata (e.g. a <c>Limit</c>
/// field) lands additively without breaking the SDK boundary.
/// </para>
/// </summary>
/// <remarks>
/// Equality is value-based on <see cref="Name"/> (record auto-generated).
/// <see cref="Name"/> is the canonical capability identifier as it appears
/// in the license catalog (e.g. <c>"multi-tenant"</c>,
/// <c>"compliance-overlay"</c>, <c>"advanced-strategies"</c>).
/// </remarks>
public sealed record CapabilityRequirement(string Name)
{
    /// <summary>
    /// Implicit conversion from a bare capability name so call sites can
    /// keep using string literals (<c>["multi-tenant"]</c>) and have the
    /// compiler materialize the record without ceremony.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    public static implicit operator CapabilityRequirement(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new CapabilityRequirement(name);
    }

    /// <summary>Returns <see cref="Name"/> so logs and diagnostics render naturally.</summary>
    public override string ToString() => Name;
}
