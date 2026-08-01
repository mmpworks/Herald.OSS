#nullable enable

using System;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Failures;
using MMP.Herald.Filters;
using MMP.Herald.Levels;
using MMP.Herald.Metrics;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Pipeline;

namespace MMP.Herald.Routing;

/// <summary>
/// Default sink builder for the core logging system.
/// Delegates sink creation to registered ILogSinkProvider instances,
/// then applies cross-cutting wrappers (retry, level filter).
/// </summary>
public sealed class DefaultLogSinkFactory : ILogSinkFactory
{
    private readonly LogSinkProviderRegistry _registry;
    private readonly ILogFailureSink _failureSink;
    private readonly LogMetricsRegistry? _metricsRegistry;

    public DefaultLogSinkFactory(
        LogSinkProviderRegistry registry,
        ILogFailureSink? failureSink = null,
        LogMetricsRegistry? metricsRegistry = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _failureSink = failureSink ?? NullLogFailureSink.Instance;
        _metricsRegistry = metricsRegistry;
    }

    public ILogger Create(
        LoggingRuntimeSinkDefinition definition,
        ILogLevelRegistry levelRegistry,
        ILogOutputTransformerRegistry transformerRegistry)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(levelRegistry);
        ArgumentNullException.ThrowIfNull(transformerRegistry);

        ILogger sink;
        try
        {
            var provider = _registry.Resolve(definition.Kind);
            sink = provider.CreateSink(definition, levelRegistry, transformerRegistry);
        }
        catch (System.Collections.Generic.KeyNotFoundException resolveFailure)
        {
            // Graceful degradation: a pipeline JSON config can outlive
            // the plugin dll that registered the sink kind (operator
            // removed the plugin, host restarted before the config
            // was edited, etc.). Crashing the whole pipeline build on
            // one missing kind is the wrong shape — the rest of the
            // sinks should keep working. Use NoOpLogger as a stand-in;
            // it implements IKernelSink so the kernel fast-path stays
            // alive on the rest of the route set, and it discards
            // every event so the pipeline behaves as if the sink were
            // disabled.
            // Failure sink is event-bound; the substitution happens at
            // build time so we surface it on stderr where boot-time
            // diagnostics already live. Operators see the warning the
            // first time the host loads / rebuilds the pipeline.
            // The registry's message carries the remedy (package name + explicit
            // registration call for first-party kinds) — surface it, not just the
            // substitution. A silent-looking no-op cost real consumers real
            // debugging time when only the bare kind name was printed.
            System.Console.Error.WriteLine(
                $"[Herald] WARN: Sink '{definition.Name}' kind '{definition.Kind}' has no registered provider. " +
                "Substituted with a no-op stand-in until the kind is restored or the pipeline is edited. " +
                resolveFailure.Message);
            return NoOpLogger.Instance;
        }

        var retriedSink = WrapWithRetryIfConfigured(sink, definition);
        var levelSink = WrapWithMinLevelIfConfigured(retriedSink, definition, levelRegistry);
        var auditSink = WrapWithAuditIfConfigured(levelSink, definition);
        return WrapWithMetricsIfConfigured(auditSink, definition);
    }

    private ILogger WrapWithRetryIfConfigured(
        ILogger sink,
        LoggingRuntimeSinkDefinition definition)
    {
        if (definition.RetryPolicy is null)
        {
            return sink;
        }

        // RetryingLogger lives in the Herald.Pro plugin assembly after
        // Step 2b-2. Resolve the wrapper factory via the SinkWrapperRegistry
        // so Core doesn't compile-reference the plugin type. If Pro isn't
        // loaded, fail loudly — a config asked for retry behaviour that
        // can't be provided.
        var factory = SinkWrapperRegistry.Resolve("retry")
            ?? throw new InvalidOperationException(
                $"Sink '{definition.Name}' has a RetryPolicy configured but no 'retry' sink-wrapper " +
                $"is registered. Reference the MMP.Herald.Pro NuGet package and ensure its plugin " +
                $"assembly is loaded (via WithPlugin<MMP.Herald.Pro.PluginBootstrap>() or a Pro " +
                $"extension method call) before building the pipeline.");
        return factory.Wrap(new SinkWrapperContext(sink, definition, _failureSink));
    }

    private ILogger WrapWithMetricsIfConfigured(
        ILogger sink,
        LoggingRuntimeSinkDefinition definition)
    {
        if (!definition.EnableMetrics || _metricsRegistry is null)
        {
            return sink;
        }

        var collector = new LogMetricsCollector(definition.Name);
        _metricsRegistry.RegisterCollector(definition.Name, collector);
        return new MetricsCapturingLogger(sink, collector);
    }

    private ILogger WrapWithAuditIfConfigured(
        ILogger sink,
        LoggingRuntimeSinkDefinition definition)
    {
        if (!definition.IsAudit)
        {
            return sink;
        }

        // AuditLogger lives in the Herald.Enterprise plugin assembly after
        // Step 2b-2. Same registry-resolved pattern as retry above.
        var factory = SinkWrapperRegistry.Resolve("audit")
            ?? throw new InvalidOperationException(
                $"Sink '{definition.Name}' is configured as IsAudit but no 'audit' sink-wrapper " +
                $"is registered. Reference the MMP.Herald.Enterprise NuGet package and ensure its " +
                $"plugin assembly is loaded (via WithPlugin<MMP.Herald.Enterprise.PluginBootstrap>() " +
                $"or an Enterprise extension method call) before building the pipeline.");
        return factory.Wrap(new SinkWrapperContext(sink, definition, _failureSink));
    }

    private static ILogger WrapWithMinLevelIfConfigured(
        ILogger sink,
        LoggingRuntimeSinkDefinition definition,
        ILogLevelRegistry levelRegistry)
    {
        if (definition.MinLevel is null)
        {
            return sink;
        }

        return new FilteringLogger(
            sink,
            [new LevelFilter(levelRegistry, definition.MinLevel)]);
    }
}
