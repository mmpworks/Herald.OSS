#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MMP.Herald.Pipeline.StepHandlers;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Instance-scoped pipeline step-handler registry. The static
/// <see cref="PipelineStepHandlerRegistry"/> facade forwards every call
/// to the default host's instance; tests and multi-tenant hosts that
/// need isolation construct their own <c>HeraldHost</c> and use
/// <c>host.StepHandlers</c> directly.
/// </summary>
public sealed class PipelineStepHandlerKindRegistry
{
    private readonly ConcurrentDictionary<string, IPipelineStepHandler> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    public PipelineStepHandlerKindRegistry()
    {
        // Register every Apache step handler. Adding a new Apache built-in
        // means editing this constructor — there is no central switch.
        // Pro/Enterprise plugin-supplied handlers register themselves from
        // their plugin assembly's [ModuleInitializer].
        Register(new FanOutStepHandler());
        Register(new SwappableStepHandler());
        Register(new AsyncStepHandler());
        Register(new RenderingStepHandler());
        Register(new BatchingStepHandler());
        Register(new FilteringStepHandler());
        Register(new PostFilteringStepHandler());
        Register(new EventProcessingStepHandler());
        Register(new FlightRecorderStepHandler());
    }

    public void Register(IPipelineStepHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentException.ThrowIfNullOrWhiteSpace(handler.StepName);
        _handlers[handler.StepName] = handler;
    }

    public IPipelineStepHandler? Resolve(string stepName)
    {
        if (string.IsNullOrWhiteSpace(stepName)) return null;
        return _handlers.TryGetValue(stepName, out var handler) ? handler : null;
    }

    public IReadOnlyCollection<string> RegisteredStepNames =>
        (IReadOnlyCollection<string>)_handlers.Keys;
}
