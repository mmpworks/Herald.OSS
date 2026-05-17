#nullable enable

using MMP.Herald.Diagnostics;
using MMP.Herald.Enrichers;
using MMP.Herald.Pipeline;
using MMP.Herald.Routing.Loopback;

namespace MMP.Herald.Quick;

/// <summary>
/// Aggregates the per-host registry surface that previously lived as
/// process-wide static classes.
///
/// <para>
/// <b>Why an instance form exists.</b> Every JSON-kind registry was a
/// <c>public static class</c>, which is the right pattern for a registry
/// (lookup-by-name + factory dispatch) but the wrong pattern for test
/// isolation and multi-tenant plugin scoping. Two parallel xUnit classes
/// that each register the same custom kind would step on each other; a
/// multi-tenant Enterprise host could not give tenant A a different
/// enricher kind than tenant B.
/// </para>
///
/// <para>
/// <b>Backward-compatible shape.</b> The static classes still exist —
/// every existing caller (Embed, Lean, Server, Sinks) keeps compiling
/// without change. Each static method now forwards to
/// <c>HeraldHost.Default.&lt;Registry&gt;.&lt;Method&gt;</c>. New code paths
/// that want isolation construct their own <see cref="HeraldHost"/> via
/// <c>new HeraldHost()</c> and consume the instance properties directly.
/// </para>
///
/// <para>
/// The host owns kind-registries AND the named-pipeline registry. The
/// static <see cref="HeraldRegistry"/> facade now forwards to
/// <c>HeraldHost.Default.Pipelines</c>, so a multi-tenant Enterprise
/// scenario can construct one host per tenant and keep the
/// <c>(tenant, name)</c> map fully isolated from other tenants.
/// </para>
/// </summary>
public sealed class HeraldHost
{
    /// <summary>
    /// The process-wide default host. Backs every <c>static</c> registry
    /// facade — <see cref="EnricherJsonRegistry"/>,
    /// <see cref="DecoratorJsonRegistry"/>,
    /// <see cref="PipelineStepHandlerRegistry"/>,
    /// <see cref="ComponentSchemaRegistry"/>, and
    /// <see cref="HeraldRegistry"/>. Created lazily on first access to
    /// keep startup ordering deterministic.
    /// </summary>
    public static HeraldHost Default { get; } = new HeraldHost();

    /// <summary>JSON enricher kind registry scoped to this host.</summary>
    public EnricherKindRegistry Enrichers { get; } = new EnricherKindRegistry();

    /// <summary>JSON pipeline-decorator kind registry scoped to this host.</summary>
    public PipelineDecoratorKindRegistry Decorators { get; } = new PipelineDecoratorKindRegistry();

    /// <summary>Pipeline step-handler registry scoped to this host.</summary>
    public PipelineStepHandlerKindRegistry StepHandlers { get; } = new PipelineStepHandlerKindRegistry();

    /// <summary>Component-schema registry scoped to this host.</summary>
    public ComponentSchemaKindRegistry ComponentSchemas { get; } = new ComponentSchemaKindRegistry();

    /// <summary>
    /// Named-pipeline registry scoped to this host. Holds the
    /// <c>(tenant → name → registration)</c> map every Herald deployment
    /// uses to look up live pipelines. The static
    /// <see cref="HeraldRegistry"/> facade forwards every call to
    /// <c>HeraldHost.Default.Pipelines</c>; tests and multi-tenant
    /// scenarios that want isolation construct their own
    /// <see cref="HeraldHost"/> and use this property directly.
    /// </summary>
    public HeraldRegistryInstance Pipelines { get; } = new HeraldRegistryInstance();

    /// <summary>
    /// Runtime-notice channel scoped to this host. Framework code
    /// (naming-policy announcement, hot-reload status, etc.)
    /// publishes here; consumers who want diagnostic visibility
    /// subscribe. The static
    /// <see cref="HeraldRuntimeMessages"/> facade forwards every
    /// call to <c>HeraldHost.Default.RuntimeMessages</c>; tests and
    /// multi-tenant scenarios that need channel isolation construct
    /// their own <see cref="HeraldHost"/> and use this property
    /// directly.
    /// </summary>
    public HeraldRuntimeMessagesInstance RuntimeMessages { get; } = new HeraldRuntimeMessagesInstance();

    /// <summary>
    /// Sink run-state holders scoped to this host. The router factory
    /// registers one <see cref="SinkRunStateHolder"/> per sink per
    /// pipeline build; the management API looks up the holder by
    /// <c>(pipelineName, sinkName)</c> to toggle run state without a
    /// pipeline rebuild. The static
    /// <see cref="SinkRunStateRegistry"/> facade forwards every call to
    /// <c>HeraldHost.Default.SinkRunState</c>; multi-host scenarios that
    /// want cross-tenant isolation (closes the principal-review #10
    /// cross-tenant interference seam) construct their own host and use
    /// this property directly.
    /// </summary>
    public SinkRunStateRegistryInstance SinkRunState { get; } = new SinkRunStateRegistryInstance();
}
