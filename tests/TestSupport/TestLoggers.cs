#nullable enable

using System.Collections.Generic;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Testing;

namespace MMP.Herald.OSS.Tests.TestSupport;

/// <summary>
/// DRY factory for test loggers. Wraps TestLogPipelineBuilder + InMemoryLogSink
/// so individual test suites do not repeat the three-line setup every time.
/// </summary>
public static class TestLoggers
{
    /// <summary>
    /// Creates a synchronous capturing pipeline at the given minimum level
    /// (defaults to Verbose, i.e. every event passes).
    ///
    /// The returned Captured list is a live snapshot view: call GetEvents()
    /// on the sink after the act phase to read accumulated events.
    /// </summary>
    public static (StructuredLogger Logger, InMemoryLogSink Sink) CreateCapturing(
        LogLevel? minimumLevel = null)
    {
        var builder = new TestLogPipelineBuilder()
            .WithMinimumLevel(minimumLevel ?? KnownLogLevels.Verbose);

        return builder.Build();
    }

    /// <summary>
    /// Typed overload — same as <see cref="CreateCapturing"/> but the logger
    /// type parameter is available for callers that want a T-carrying type
    /// context. The level semantics are identical.
    /// </summary>
    public static (StructuredLogger Logger, InMemoryLogSink Sink) CreateCapturing<T>(
        LogLevel? minimumLevel = null)
    {
        return CreateCapturing(minimumLevel);
    }

    /// <summary>
    /// Convenience accessor: all events captured by the sink as a snapshot list.
    /// Same as sink.GetEvents() but lets a test write CreateCapturing() once and
    /// not keep the InMemoryLogSink reference around when the logger is sufficient.
    /// </summary>
    public static IReadOnlyList<LogEvent> GetCaptured(InMemoryLogSink sink)
    {
        return sink.GetEvents();
    }
}
