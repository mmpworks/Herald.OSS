#nullable enable

using System;

namespace MMP.Herald.Pipeline;

/// <summary>
/// SDK-boundary record naming a single capability a component requires to run.
/// The plugin-author-facing surface for the rev 2 capability-composition plan
/// (Rosanne S-3) — components declare their requirements as
/// <c>IReadOnlyList&lt;CapabilityRequirement&gt;</c>; the runtime gate
/// (<see cref="MMP.Herald.HeraldCapabilityGate"/>) reads them in declaration
/// order and throws on the first missing cap.
///
/// <para>
/// The record carries only <see cref="Name"/> today. The record-with-room-to-grow
/// shape (rather than bare <c>string</c>) is the door Rosanne S-3 cut so future
/// additions — per-cap minimum quantity, per-cap profile / mode, per-cap
/// lookup-only sentinel — land as additional properties without touching call
/// sites. The wire format on the license token stays flat in v1
/// (<c>caps: ["multi-tenant"]</c>); per-cap-metadata is SDK-only.
/// </para>
///
/// <para>
/// <b>Implicit string conversion.</b> A string literal converts implicitly
/// to <see cref="CapabilityRequirement"/>, so existing-style collection-expression
/// declarations stay terse:
/// </para>
///
/// <code>
/// public IReadOnlyList&lt;CapabilityRequirement&gt; RequiredCapabilities =>
///     ["multi-tenant", "compliance-overlay"]; // implicit conversion from string
/// </code>
///
/// <para>
/// Capability names follow a hyphenated lowercase convention
/// (<c>multi-tenant</c>, <c>compliance-overlay</c>, <c>ffiec-chain</c>) for
/// MMPWorks-canonical capabilities. Third-party vendors should namespace their
/// capabilities as <c>&lt;vendor&gt;.&lt;cap&gt;</c> (e.g. <c>acme.advanced-redaction</c>)
/// to avoid collisions with the closed MMPWorks catalog
/// (<see cref="HeraldCapabilityGate.ValidateCapabilityNameShape(string)"/>).
/// </para>
/// </summary>
/// <remarks>
/// CUPID Composable: the record exists so plugin authors can compose their
/// component's capability surface from named primitives without inheriting
/// from a base class.
/// </remarks>
public sealed record CapabilityRequirement(string Name)
{
    /// <summary>
    /// Implicit conversion from a bare capability name so call sites stay
    /// concise — <c>["multi-tenant"]</c> and
    /// <c>[new CapabilityRequirement("multi-tenant")]</c> both compile.
    /// </summary>
    public static implicit operator CapabilityRequirement(string name) => new(name);

    /// <summary>
    /// Returns the capability name verbatim. Diagnostic messages and
    /// dashboard rendering both rely on this rather than the default
    /// record-formatted <c>CapabilityRequirement { Name = ... }</c>.
    /// </summary>
    public override string ToString() => Name;
}
