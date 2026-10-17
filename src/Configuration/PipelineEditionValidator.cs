#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MMP.Herald.Pipeline;

namespace MMP.Herald.Configuration;

/// <summary>
/// Bootstrap-time validator that checks every component in an assembled
/// <see cref="LogPipelinePolicy"/> against the running
/// <see cref="HeraldEdition"/>, produces a single composed error when any
/// component exceeds the edition, and fails fast with an actionable
/// message.
///
/// <para>
/// Before the validator, edition gating happened per-component: a
/// misconfigured pipeline that combined three Enterprise-only components
/// on a Community build surfaced as the first-to-register throwing
/// alone. Operators had to fix, rebuild, and hit the next one. The
/// validator walks the whole policy once and reports every
/// incompatibility together so a single fix pass clears the chain.
/// </para>
///
/// <para>
/// Scope today:
/// <list type="bullet">
///   <item><see cref="PipelineStrategy"/> steps — every <see cref="PipelineStep.MinimumEdition"/> listed in the strategy is checked.</item>
///   <item>Event processors that implement <see cref="IComponentMetadata"/> — their <see cref="IComponentMetadata.MinimumEdition"/> is checked.</item>
///   <item>Custom decorators that implement <see cref="IComponentMetadata"/> — same treatment.</item>
/// </list>
/// Sinks are not walked at this level because they are not yet attached
/// to the policy when the validator runs; sink edition gating happens at
/// the sink-provider registration step and naturally produces a single
/// failure per sink.
/// </para>
///
/// <para><b>Threading.</b> Stateless; safe to call from any bootstrap
/// thread.</para>
///
/// <para><b>Tests.</b> <c>tests/Configuration/PipelineEditionValidatorTests.cs</c>.</para>
/// </summary>
public static class PipelineEditionValidator
{
    /// <summary>
    /// Validate <paramref name="policy"/> against the current edition.
    /// Throws <see cref="InvalidOperationException"/> with a composed
    /// multi-line message listing every incompatible component when any
    /// fail. Returns silently on success.
    /// </summary>
    public static void Validate(LogPipelinePolicy policy) =>
        ValidateAgainst(policy, HeraldVersion.CurrentEdition);

    /// <summary>
    /// Test-only overload that drives the validator against an arbitrary
    /// "current edition" instead of <see cref="HeraldVersion.CurrentEdition"/>.
    /// Production callers use <see cref="Validate(LogPipelinePolicy)"/>.
    /// </summary>
    internal static void ValidateAgainst(LogPipelinePolicy policy, HeraldEdition currentEdition)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(currentEdition);

        var failures = new List<string>();

        // Strategy steps — the declarative composition surface. HotPath
        // and any other higher-edition steps show up here when callers pick
        // a non-default strategy. (FlightRecorder is Community-tier now —
        // the decorator's MinEdition is Community; configurability is gated
        // at the WithFlightRecorder / JSON mapper boundary instead.)
        //
        // A step is validated only when it will actually contribute work
        // to the assembled pipeline. eventProcessing is a no-op when no
        // processors are registered, and flightRecorder is dormant when
        // FlightRecorderPolicy is null — flagging either as an edition
        // violation would make the default strategy unusable when the
        // operator is not using the optional feature.
        if (policy.Strategy is not null)
        {
            foreach (var step in policy.Strategy.Steps)
            {
                if (currentEdition.Includes(step.MinimumEdition)) continue;
                if (IsDormantStep(step, policy)) continue;

                failures.Add(
                    $"  - pipeline step '{step.Name}' requires {step.MinimumEdition.Name} (current: {currentEdition.Name})");
            }
        }

        // Event processors — each one that self-reports via IComponentMetadata.
        if (policy.EventProcessors is { Count: > 0 })
        {
            foreach (var processor in policy.EventProcessors)
            {
                if (processor is IComponentMetadata meta &&
                    !currentEdition.Includes(meta.MinimumEdition))
                {
                    failures.Add(
                        $"  - event processor '{meta.ComponentName}' requires {meta.MinimumEdition.Name} (current: {currentEdition.Name})");
                }
            }
        }

        // Custom decorators — plugin authors implementing IComponentMetadata
        // get the same treatment as built-in components.
        if (policy.CustomDecorators is { Count: > 0 })
        {
            foreach (var decorator in policy.CustomDecorators)
            {
                if (decorator is IComponentMetadata meta &&
                    !currentEdition.Includes(meta.MinimumEdition))
                {
                    failures.Add(
                        $"  - custom decorator '{meta.ComponentName}' requires {meta.MinimumEdition.Name} (current: {currentEdition.Name})");
                }
            }
        }

        if (failures.Count == 0) return;

        var message = new StringBuilder()
            .Append("Pipeline composition is incompatible with the current ")
            .Append(currentEdition.Name)
            .Append(" edition. ")
            .Append(failures.Count)
            .AppendLine(failures.Count == 1 ? " component exceeds the edition:" : " components exceed the edition:");
        foreach (var line in failures)
        {
            message.AppendLine(line);
        }
        message
            .Append("Rebuild with a higher HeraldEdition, remove the listed components, or pick a strategy that excludes them.");

        throw new InvalidOperationException(message.ToString());
    }

    /// <summary>
    /// A step is dormant when its handler does not add a decorator under
    /// the current policy. Two known-dormant cases today:
    /// <list type="bullet">
    ///   <item><c>eventProcessing</c> — no-op when <see cref="LogPipelinePolicy.EventProcessors"/> is null or empty.</item>
    ///   <item><c>flightRecorder</c> — no-op when <see cref="LogPipelinePolicy.FlightRecorderPolicy"/> is null. The step itself is Community-tier, so dormancy is academic for the edition gate, but the carve-out keeps the validator consistent with the eventProcessing pattern.</item>
    /// </list>
    /// Other steps always add a decorator (or are the terminal fan-out),
    /// so absent from this list means "will contribute" and their
    /// <see cref="PipelineStep.MinimumEdition"/> is enforced.
    /// </summary>
    private static bool IsDormantStep(PipelineStep step, LogPipelinePolicy policy)
    {
        // Step names compared as literals so this method does not depend on
        // the plugin-supplied PipelineStep singletons being registered. When a
        // plugin isn't loaded, the step never appears in the strategy anyway.
        if (string.Equals(step.Name, "eventProcessing", StringComparison.OrdinalIgnoreCase))
        {
            return policy.EventProcessors is not { Count: > 0 };
        }
        if (string.Equals(step.Name, "flightRecorder", StringComparison.OrdinalIgnoreCase))
        {
            return policy.FlightRecorderPolicy is null;
        }
        return false;
    }
}
