#nullable enable

using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Failures;

namespace MMP.Herald.Routing;

/// <summary>
/// Factory that wraps an individual sink with a decorator (retry,
/// audit, etc.) based on per-sink configuration. Plugins register
/// their wrapper factories via <see cref="SinkWrapperRegistry"/> so
/// Core's sink factory can apply them without compile-time references
/// to the plugin's logger types.
///
/// <para>
/// Symmetric to the pipeline-step plumbing: Core defines the contract
/// and orchestrates dispatch by kind name; plugin assemblies supply
/// implementations and register them at module-init time.
/// </para>
/// </summary>
public interface ISinkWrapperFactory
{
    /// <summary>
    /// Kind name. Matches the configuration discriminator (e.g.,
    /// "retry" for definitions whose RetryPolicy is set, "audit" for
    /// definitions whose IsAudit is true).
    /// </summary>
    string Kind { get; }

    /// <summary>
    /// Wrap <paramref name="inner"/> with this factory's decorator,
    /// reading configuration from <paramref name="context"/>.
    /// </summary>
    ILogger Wrap(SinkWrapperContext context);
}

/// <summary>
/// Context passed to <see cref="ISinkWrapperFactory.Wrap"/>. A readonly
/// struct so the wrap path stays allocation-free.
/// </summary>
public readonly struct SinkWrapperContext
{
    public SinkWrapperContext(
        ILogger inner,
        LoggingRuntimeSinkDefinition definition,
        ILogFailureSink failureSink)
    {
        Inner = inner;
        Definition = definition;
        FailureSink = failureSink;
    }

    /// <summary>The sink being wrapped.</summary>
    public ILogger Inner { get; }

    /// <summary>Per-sink configuration that carries policy fields the wrapper reads (RetryPolicy, IsAudit, etc.).</summary>
    public LoggingRuntimeSinkDefinition Definition { get; }

    /// <summary>Shared failure sink for wrappers that report wrap-internal diagnostics (e.g., AuditLogger on throw).</summary>
    public ILogFailureSink FailureSink { get; }
}
