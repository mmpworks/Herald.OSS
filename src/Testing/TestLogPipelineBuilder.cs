#nullable enable

using System.Collections.Generic;
using MMP.Herald.Enrichers;
using MMP.Herald.Events;
using MMP.Herald.Filters;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Templating;
using MMP.Herald.Time;

namespace MMP.Herald.Testing;

/// <summary>
/// Fluent builder for constructing minimal, synchronous test pipelines.
/// Produces a StructuredLogger backed by an InMemoryLogSink with no async or batching,
/// ensuring deterministic behavior in tests.
/// </summary>
public sealed class TestLogPipelineBuilder
{
    private LogLevel? _minimumLevel;
    private readonly List<ILogFilter> _filters = new();
    private readonly List<ILogEnricher> _enrichers = new();
    private ILogLevelRegistry? _levelRegistry;
    private IDateTimeProvider? _dateTimeProvider;

    public TestLogPipelineBuilder WithMinimumLevel(LogLevel level)
    {
        _minimumLevel = level;
        return this;
    }

    public TestLogPipelineBuilder WithFilter(ILogFilter filter)
    {
        _filters.Add(filter);
        return this;
    }

    public TestLogPipelineBuilder WithEnricher(ILogEnricher enricher)
    {
        _enrichers.Add(enricher);
        return this;
    }

    public TestLogPipelineBuilder WithLevelRegistry(ILogLevelRegistry registry)
    {
        _levelRegistry = registry;
        return this;
    }

    public TestLogPipelineBuilder WithDateTimeProvider(IDateTimeProvider provider)
    {
        _dateTimeProvider = provider;
        return this;
    }

    /// <summary>
    /// Builds a synchronous test pipeline. Returns the logger and the in-memory sink
    /// for capturing and asserting against logged events.
    /// </summary>
    public (StructuredLogger Logger, InMemoryLogSink Sink) Build()
    {
        var levelRegistry = _levelRegistry ?? BuildDefaultLevelRegistry();
        var sink = new InMemoryLogSink();

        ILogger pipeline = sink;

        if (_minimumLevel is not null || _filters.Count > 0)
        {
            var allFilters = new List<ILogFilter>();

            if (_minimumLevel is not null)
            {
                allFilters.Add(new LevelFilter(levelRegistry, _minimumLevel));
            }

            allFilters.AddRange(_filters);
            pipeline = new FilteringLogger(pipeline, allFilters);
        }

        var enricher = _enrichers.Count > 0
            ? (ILogEnricher)new CompositeLogEnricher([.. _enrichers])
            : NullLogEnricher.Instance;

        var dateTimeProvider = _dateTimeProvider ?? new SystemDateTimeProvider();
        var scopeProvider = new AsyncLocalLogScopeProvider();

        var eventFactory = new LogEventFactory(
            dateTimeProvider,
            new MessageTemplateParser(),
            scopeProvider,
            enricher);

        var logger = new StructuredLogger(
            pipeline,
            eventFactory,
            scopeProvider,
            levelRegistry: levelRegistry,
            minimumLevel: _minimumLevel);

        return (logger, sink);
    }

    private static ILogLevelRegistry BuildDefaultLevelRegistry()
    {
        var registry = new LogLevelRegistry();
        registry.Register(KnownLogLevels.Trace);
        registry.Register(KnownLogLevels.Debug);
        registry.Register(KnownLogLevels.Info);
        registry.Register(KnownLogLevels.Warn);
        registry.Register(KnownLogLevels.Error);
        return registry;
    }
}
