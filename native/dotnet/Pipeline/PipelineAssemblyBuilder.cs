#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration;
using MMP.Herald.Events;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Metrics;
using MMP.Herald.Templating;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Fluent builder that assembles the logging pipeline decorator chain.
/// Tracks async resources and applies decorators in the correct order,
/// eliminating the async/sync branching that previously existed in DefaultLogPipelineFactory.
/// </summary>
public sealed class PipelineAssemblyBuilder
{
    private ILogger _pipeline;
    private readonly List<IAsyncDisposable> _asyncResources = new();
    private readonly PipelineAccessor? _accessor;
    private SwappableLogger? _swappable;

    public PipelineAssemblyBuilder(ILogger initialPipeline, PipelineAccessor? accessor = null)
    {
        _pipeline = initialPipeline ?? throw new ArgumentNullException(nameof(initialPipeline));
        _accessor = accessor;
    }

    /// <summary>The current head of the pipeline chain being assembled.</summary>
    internal ILogger CurrentPipeline => _pipeline;

    /// <summary>Replace the current pipeline head. Used by strategy-driven assembly.</summary>
    internal void SetPipeline(ILogger pipeline) => _pipeline = pipeline;

    public PipelineAssemblyBuilder TrackAsyncResource(IAsyncDisposable? resource)
    {
        if (resource is not null)
        {
            _asyncResources.Add(resource);
        }

        return this;
    }

    public PipelineAssemblyBuilder WithBatching(
        BatchingPolicy policy,
        ILogFailureSink failureSink)
    {
        if (!policy.IsEnabled)
        {
            return this;
        }

        var batchingLogger = new BatchingLogger(
            _pipeline,
            failureSink,
            policy.MaxBatchSize,
            policy.MaxBatchDelayMs);

        _pipeline = batchingLogger;
        _asyncResources.Add(batchingLogger);
        _accessor?.Register(batchingLogger);
        return this;
    }

    public PipelineAssemblyBuilder WithAsync(
        AsyncLogPolicy policy,
        ILogFailureSink failureSink,
        IPipelineDropSink? dropSink = null)
    {
        if (!policy.IsEnabled)
        {
            return this;
        }

        var asyncLogger = new AsyncLogger(
            _pipeline,
            failureSink,
            policy.Capacity,
            policy.DropStrategy,
            dropSink: dropSink);

        _pipeline = asyncLogger;
        _asyncResources.Insert(0, asyncLogger);
        _accessor?.Register(asyncLogger);
        return this;
    }

    /// <summary>
    /// Insert a RenderingLogger that renders deferred events on the worker thread.
    /// Must be called before WithAsync() so the renderer sits inside the async boundary.
    /// </summary>
    public PipelineAssemblyBuilder WithDeferredRendering(MessageTemplateParser parser) {
        var rendering = new RenderingLogger(_pipeline, parser);
        _pipeline = rendering;
        _accessor?.Register(rendering);
        return this;
    }

    public PipelineAssemblyBuilder WithSwappable(bool hotReloadEnabled)
    {
        if (!hotReloadEnabled)
        {
            return this;
        }

        _swappable = new SwappableLogger(_pipeline);
        _pipeline = _swappable;
        _accessor?.Register(_swappable);
        return this;
    }

    public LoggerComposition Build(
        ILogEventFactory eventFactory,
        ILogScopeProvider scopeProvider,
        bool includeCallerInfo,
        ILogLevelRegistry levelRegistry,
        LogLevel minimumLevel,
        Kernel.LogKernel? kernel = null,
        MMP.Herald.Time.IDateTimeProvider? dateTimeProvider = null,
        MMP.Herald.Templating.IPropertyNamingPolicy? namingPolicy = null)
    {
        var logger = new StructuredLogger(
            _pipeline,
            eventFactory,
            scopeProvider,
            includeCallerInfo: includeCallerInfo,
            levelRegistry: levelRegistry,
            minimumLevel: minimumLevel,
            kernel: kernel,
            dateTimeProvider: dateTimeProvider,
            namingPolicy: namingPolicy);

        _accessor?.Register(logger);

        IAsyncDisposable? asyncResource = _asyncResources.Count switch
        {
            0 => null,
            1 => _asyncResources[0],
            _ => new AsyncResourceGroup(_asyncResources)
        };

        return new LoggerComposition(logger, asyncResource, _swappable);
    }
}
