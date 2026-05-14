#nullable enable

using System;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Quick;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Instance-scoped pipeline-decorator kind registry. The static
/// <see cref="DecoratorJsonRegistry"/> facade forwards every call to the
/// default host's instance of this class; tests and multi-tenant hosts
/// that need isolation construct their own <see cref="HeraldHost"/> and
/// use <c>host.Decorators</c> directly.
/// </summary>
public sealed class PipelineDecoratorKindRegistry : JsonKindRegistry<IConfigurablePipelineDecorator>
{
    /// <summary>
    /// Reconstruct a decorator from its JSON config. Convenience over the
    /// base <c>Reconstruct(kind, properties)</c> for the common
    /// <see cref="JsonPipelineDecoratorConfig"/>-shaped call site.
    /// </summary>
    public IConfigurablePipelineDecorator Reconstruct(JsonPipelineDecoratorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return Reconstruct(config.Kind, config.Properties);
    }

    protected override string BuildUnknownKindMessage(string kind) =>
        $"Unknown pipeline decorator kind '{kind}'. " +
        "Custom decorators are plugin-supplied; the plugin must call " +
        "DecoratorJsonRegistry.Register(kind, factory) — or, for an isolated host, " +
        "host.Decorators.Register(kind, factory) — at plugin initialization. " +
        "Without registration, a JSON-driven Reload cannot rebuild the decorator chain " +
        "and the host's custom decorators silently drop out of the pipeline.";
}
