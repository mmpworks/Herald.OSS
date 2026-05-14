#nullable enable

using MMP.Herald.Events;
using MMP.Herald.Levels;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Per-route kernel-compatible wrapper that admits events whose level is at
/// or above a configured floor. Compiled from a
/// <see cref="Predicates.PredicateSpec.LevelAtOrAbove"/> route predicate so
/// the kernel can fan out directly without falling back to the decorator
/// chain for level-based routes.
/// </summary>
public sealed class LevelFilteredKernelSink : ILogger, IKernelSink
{
    private readonly IKernelSink _innerKernel;
    private readonly ILogger _innerLogger;
    private readonly ILogLevelRegistry _levelRegistry;
    private readonly LogLevel _minimumLevel;

    public LevelFilteredKernelSink(
        ILogger inner,
        ILogLevelRegistry levelRegistry,
        LogLevel minimumLevel)
    {
        System.ArgumentNullException.ThrowIfNull(inner);
        System.ArgumentNullException.ThrowIfNull(levelRegistry);
        System.ArgumentNullException.ThrowIfNull(minimumLevel);

        _innerLogger = inner;
        _innerKernel = inner as IKernelSink
            ?? throw new System.ArgumentException(
                $"LevelFilteredKernelSink requires an inner sink that implements IKernelSink; got {inner.GetType().Name}.",
                nameof(inner));
        _levelRegistry = levelRegistry;
        _minimumLevel = minimumLevel;
    }

    public void Log(LogEvent logEvent)
    {
        System.ArgumentNullException.ThrowIfNull(logEvent);
        if (!_levelRegistry.IsAtOrAbove(logEvent.Level, _minimumLevel)) return;
        _innerLogger.Log(logEvent);
    }

    public void Log(in LogEventBuffer buffer)
    {
        if (!_levelRegistry.IsAtOrAbove(buffer.Level, _minimumLevel)) return;
        _innerKernel.Log(in buffer);
    }

    // -- Inspection --

    /// <summary>The minimum level admitted by this sink.</summary>
    public LogLevel MinimumLevel => _minimumLevel;

    /// <summary>The downstream sink that admitted events are forwarded to.</summary>
    public ILogger Inner => _innerLogger;
}
