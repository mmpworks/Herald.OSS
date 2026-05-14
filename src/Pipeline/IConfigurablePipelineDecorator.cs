#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration;
using MMP.Herald.Routing;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Plugin interface for custom pipeline decorators. Implement this to create
/// a decorator that plugs into the pipeline strategy alongside built-in steps
/// (Async, Filtering, Batching, etc.) and is discoverable + configurable
/// by the dashboard.
///
/// The decorator participates in the pipeline assembly: when the strategy
/// walker encounters a step matching <see cref="StepName"/>, it calls
/// <see cref="CreateDecorator"/> to wrap the current pipeline.
///
/// Usage:
///   // 1. Implement the interface
///   public class RateLimiterDecorator : IConfigurablePipelineDecorator
///   {
///       public string StepName => "rateLimiter";
///       public string DisplayName => "Rate Limiter";
///       public string Description => "Limits events per second per category";
///       public IReadOnlyList&lt;SinkConfigField&gt; ConfigurationSchema => [
///           SinkConfigField.Int("maxPerSecond", 1000, "Max events/second"),
///       ];
///       // ...
///   }
///
///   // 2. Register the custom step
///   PipelineStep.Register("rateLimiter");
///
///   // 3. Register the decorator with the builder
///   builder.WithPipelineDecorator(new RateLimiterDecorator());
///
///   // 4. Use it in a strategy
///   builder.WithPipelineStrategy(PipelineStrategy.Create()
///       .Swappable().Filtering().Custom("rateLimiter").Async().FanOut());
///
///   // 5. Dashboard discovers it via GET /api/pipelineDecorators
/// </summary>
public interface IConfigurablePipelineDecorator : IComponentMetadata
{
    /// <summary>
    /// Step name matching a <see cref="PipelineStep"/>. Must be unique.
    /// Convention: camelCase, no spaces (e.g., "rateLimiter", "circuitBreaker").
    /// </summary>
    string StepName { get; }

    // IComponentMetadata is satisfied by mapping:
    //   ComponentName → StepName
    //   DisplayName   → (inherited)
    //   Description   → (inherited)
    //   Help          → auto-generated from Description + Schema by the builder
    //   Vendor        → auto-extracted from assembly namespace by the builder
    //   ConfigurationSchema → (inherited)

    /// <inheritdoc cref="IComponentMetadata.ComponentName"/>
    string IComponentMetadata.ComponentName => StepName;

    /// <summary>
    /// Help text. Defaults to Description. Override for richer documentation.
    /// The builder auto-enriches this with schema field details.
    /// </summary>
    string IComponentMetadata.Help => Description;

    /// <summary>
    /// Vendor info. Defaults to VendorInfo.ThirdParty.
    /// Override to provide rich vendor metadata (name, license, contact).
    /// </summary>
    VendorInfo IComponentMetadata.Vendor => VendorInfo.ThirdParty;

    /// <summary>Get current configuration values.</summary>
    IReadOnlyDictionary<string, object?> GetConfiguration();

    /// <summary>Apply configuration values. Returns (true, null) on success.</summary>
    (bool Success, string? Error) ApplyConfiguration(IReadOnlyDictionary<string, object?> values);

    /// <summary>
    /// Create the decorator. Called during pipeline assembly when the strategy
    /// walker encounters a step matching <see cref="StepName"/>.
    /// The decorator wraps <paramref name="inner"/> and returns the new pipeline head.
    /// </summary>
    /// <param name="inner">The downstream pipeline to wrap.</param>
    /// <param name="pipelineAccessor">Register yourself here for introspection.</param>
    ILogger CreateDecorator(ILogger inner, PipelineAccessor? pipelineAccessor);

    /// <summary>
    /// Snapshot the decorator's identity + current configuration for JSON
    /// emission. The default implementation pairs <see cref="StepName"/>
    /// (which doubles as the kind for registry lookup) with
    /// <see cref="GetConfiguration"/>'s output, so plugin authors typically
    /// don't need to override this. Override only when the JSON shape needs
    /// to differ from the live configuration dict.
    ///
    /// <para>
    /// On Reload, the matching <c>DecoratorJsonRegistry.Register(kind, factory)</c>
    /// entry consumes the <c>Properties</c> dict to rebuild the decorator.
    /// </para>
    /// </summary>
    Configuration.Json.JsonPipelineDecoratorConfig ToJsonConfig()
    {
        var props = GetConfiguration();
        return new Configuration.Json.JsonPipelineDecoratorConfig(
            StepName,
            props.Count > 0 ? props : null);
    }
}
