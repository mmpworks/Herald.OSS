#nullable enable

using System;
using MMP.Herald.Quick;

namespace MMP.Herald.Routing.Loopback;

/// <summary>
/// Process-wide static facade over
/// <see cref="HeraldHost.Default"/>'s
/// <see cref="SinkRunStateRegistryInstance"/>. Every call forwards
/// to that instance — the same pattern <see cref="HeraldRegistry"/>
/// and <see cref="MMP.Herald.Diagnostics.HeraldRuntimeMessages"/>
/// use to give the process a single well-known surface while
/// keeping the actual state on a per-host instance.
///
/// <para>
/// <b>Deprecated for multi-host scenarios.</b> Two
/// <see cref="HeraldHost"/> instances on the same process previously
/// shared this map; tenant A's <c>ApplySinkRuntime</c> PATCH could land
/// on tenant B's holder if pipeline names collided (principal-review
/// queue #10). Construct a dedicated <see cref="HeraldHost"/> and use
/// <c>host.SinkRunState</c> directly for isolation. Existing single-
/// host callers keep compiling through this facade.
/// </para>
///
/// <para>
/// <b>Why no <c>[Obsolete]</c> attribute.</b> Herald.OSS builds with
/// <c>TreatWarningsAsErrors=true</c>. An <c>[Obsolete]</c> on this
/// facade would harden into CS0618 errors at every internal call site
/// (router factory, management API) the moment they compile, blocking
/// the single-host happy path the facade was kept for. The shape
/// matches <see cref="HeraldRegistry"/> and
/// <see cref="MMP.Herald.Diagnostics.HeraldRuntimeMessages"/>, which
/// take the same deprecation-in-docs-only stance for the same reason.
/// </para>
/// </summary>
public static class SinkRunStateRegistry
{
    /// <summary>
    /// Register (or replace) the holder for one sink on the default host.
    /// Forwards to <c>HeraldHost.Default.SinkRunState.Register</c>.
    /// </summary>
    public static void Register(string pipelineName, string sinkName, SinkRunStateHolder holder) =>
        HeraldHost.Default.SinkRunState.Register(pipelineName, sinkName, holder);

    /// <summary>
    /// Look up a holder on the default host. Forwards to
    /// <c>HeraldHost.Default.SinkRunState.Get</c>.
    /// </summary>
    public static SinkRunStateHolder? Get(string pipelineName, string sinkName) =>
        HeraldHost.Default.SinkRunState.Get(pipelineName, sinkName);

    /// <summary>
    /// Remove every holder for a pipeline on the default host. Forwards
    /// to <c>HeraldHost.Default.SinkRunState.ClearPipeline</c>.
    /// </summary>
    public static void ClearPipeline(string pipelineName) =>
        HeraldHost.Default.SinkRunState.ClearPipeline(pipelineName);
}
