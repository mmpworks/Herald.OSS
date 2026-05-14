#nullable enable

using System.Collections.Generic;
using MMP.Herald.Quick;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Registry that maps <see cref="PipelineStep.Name"/> → <see cref="IPipelineStepHandler"/>.
///
/// <para>
/// Built-in handlers (async, batching, filtering, swappable, rendering,
/// event-processing, post-filtering, flight-recorder, fan-out) self-register
/// from the default host's <see cref="PipelineStepHandlerKindRegistry"/> on
/// first access. Plugins call <see cref="Register"/> at bootstrap; the
/// registry is a <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>
/// so two plugins initialising in parallel cannot corrupt the bucket
/// chain — same thread-safety posture as the rest of the registry family.
/// </para>
///
/// <para>
/// <b>Hosting model.</b> Forwards every call to <see cref="HeraldHost.Default"/>'s
/// <see cref="PipelineStepHandlerKindRegistry"/>. Tests and multi-tenant
/// hosts that want isolated registry state construct their own
/// <c>HeraldHost</c> and consume <c>host.StepHandlers</c> directly.
/// </para>
/// </summary>
public static class PipelineStepHandlerRegistry
{
    /// <summary>
    /// Add or replace the handler for a step. Replacement is deliberate:
    /// a plugin that wants to customise a built-in step's assembly behaviour
    /// can register under the same name. Indexer assignment is thread-safe
    /// on <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>.
    /// </summary>
    public static void Register(IPipelineStepHandler handler) =>
        HeraldHost.Default.StepHandlers.Register(handler);

    /// <summary>
    /// Look up the handler for <paramref name="stepName"/>. Returns null when
    /// no handler is registered; the caller falls through to custom-decorator
    /// resolution on <see cref="LogPipelinePolicy.CustomDecorators"/>.
    /// </summary>
    public static IPipelineStepHandler? Resolve(string stepName) =>
        HeraldHost.Default.StepHandlers.Resolve(stepName);

    /// <summary>
    /// Snapshot of currently-registered step names. Useful for diagnostics
    /// and for the Dashboard's pipeline editor to render the palette of
    /// known steps.
    /// </summary>
    public static IReadOnlyCollection<string> RegisteredStepNames =>
        HeraldHost.Default.StepHandlers.RegisteredStepNames;
}
