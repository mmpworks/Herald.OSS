#nullable enable

using System;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Marks a <c>LogProperty.Lazy(...)</c> factory closure (or a method that
/// produces one) as REVIEWED-AND-SAFE for the async-sink drain thread.
/// Suppresses HERALD008-HERALD013 on the marked target.
///
/// <para>
/// <b>Reason is required.</b> The attribute's <see cref="Reason"/> property
/// must be a non-empty string explaining WHY the closure is safe to defer
/// (e.g. <c>"reads immutable config; no AsyncLocal capture"</c>). Empty
/// reasons defeat the audit trail the attribute exists to provide.
/// </para>
///
/// <para>
/// <b>Build-output audit note.</b> Every use of this attribute emits an
/// informational diagnostic at build time so the count of reviewed
/// suppressions appears in build logs. This converts the "I'll just slap
/// the attribute on it" pattern into a visible audit trail — the reason
/// string is the reviewer's signature.
/// </para>
///
/// <para>
/// <b>Layered defense.</b> The L1 / L2 / L4 fixes in 0.10.2 already
/// eager-resolve lazy factories on the producer thread, so this attribute
/// is the explicit opt-out for the rare closure that the author has
/// proven thread-safe for the drain thread. The default is "no
/// suppressions"; reaching for this attribute should always involve
/// a security review.
/// </para>
/// </summary>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false,
    Inherited = false)]
public sealed class HeraldDrainSafeAttribute : Attribute
{
    /// <summary>
    /// The reviewer's reason this closure is safe to defer past the
    /// producer-thread boundary. Required. The Roslyn analyzer that
    /// honours <see cref="HeraldDrainSafeAttribute"/> as a suppressor
    /// rejects empty / null reasons and emits an informational build
    /// note for every accepted suppression.
    /// </summary>
    public string Reason { get; }

    public HeraldDrainSafeAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "HeraldDrainSafe requires a non-empty Reason string explaining " +
                "why this closure is safe to defer past the producer-thread " +
                "boundary. The reason is the reviewer's signature on the audit " +
                "trail; empty reasons defeat the attribute's purpose.",
                nameof(reason));
        }
        Reason = reason;
    }
}
