#nullable enable

using MMP.Herald.Events;
using MMP.Herald.Pipeline.Kernel;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Logger that silently discards all events. Used as a temporary proxy
/// when rebuilding a non-hot-swappable pipeline (e.g. HotPath) and as the
/// terminus for <see cref="MMP.Herald.Quick.QuickLogBuilder.WithNullSink"/>
/// benchmarks and muted configurations.
///
/// Implements <see cref="IKernelSink"/> so the kernel pipeline can dispatch
/// buffers directly without materialising a heap <see cref="LogEvent"/>.
/// Both <see cref="Log(LogEvent)"/> and <see cref="Log(in LogEventBuffer)"/>
/// are intentional no-ops.
///
/// During a rebuild window, all log events are lost. This is the accepted
/// tradeoff for pipelines that prioritize speed over hot-reload.
/// </summary>
public sealed class NoOpLogger : ILogger, IKernelSink, IStructuredOnlySink
{
    public static readonly NoOpLogger Instance = new();

    private NoOpLogger() { }

    public void Log(LogEvent logEvent)
    {
        // Intentionally empty — events are silently dropped.
    }

    public void Log(in LogEventBuffer buffer)
    {
        // Intentionally empty — kernel-path events are silently dropped.
    }
}
