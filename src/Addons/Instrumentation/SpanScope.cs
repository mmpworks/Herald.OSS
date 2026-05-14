#nullable enable

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Spans;
using MMP.Herald.Templating;

namespace MMP.Herald.Addons.Instrumentation;

/// <summary>
/// Creates an instrumented span scope around a block of code.
/// Inspired by Rust tracing's #[instrument] attribute.
///
/// Automatically captures: method name, file, line, execution duration.
/// Logs entry at Debug and exit at Debug (with elapsed time).
///
/// Usage:
///   using var span = SpanScope.Begin(logger, spanFactory);
///   // ... code being instrumented ...
///   // span auto-completes on dispose with duration
///
/// Or with explicit name:
///   using var span = SpanScope.Begin(logger, spanFactory, "ProcessOrder");
/// </summary>
public sealed class SpanScope : IDisposable
{
    private readonly StructuredLogger _logger;
    private readonly LogSpan _span;
    private readonly string _operationName;
    private readonly long _startTicks;

    private SpanScope(StructuredLogger logger, LogSpan span, string operationName) {
        _logger = logger;
        _span = span;
        _operationName = operationName;
        _startTicks = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Begin an instrumented span scope. Automatically captures caller info.
    /// </summary>
    public static SpanScope Begin(
        StructuredLogger logger,
        LogSpanFactory spanFactory,
        string? operationName = null,
        [CallerMemberName] string? callerMember = null,
        [CallerFilePath] string? callerFile = null,
        [CallerLineNumber] int callerLine = 0) {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(spanFactory);

        var name = operationName ?? callerMember ?? "unknown";
        var span = spanFactory.Begin(name);

        logger.Debug(LogCategory.App, "-> {operation} [{file}:{line}]",
            properties: [
                new LogProperty("operation", name),
                new LogProperty("file", System.IO.Path.GetFileName(callerFile)),
                new LogProperty("line", callerLine)
            ]);

        return new SpanScope(logger, span, name);
    }

    public void Dispose() {
        var elapsed = Stopwatch.GetElapsedTime(_startTicks);
        _span.Dispose();

        _logger.Debug(LogCategory.App, "<- {operation} completed in {elapsedMs}ms",
            properties: [
                new LogProperty("operation", _operationName),
                new LogProperty("elapsedMs", elapsed.TotalMilliseconds)
            ]);
    }
}
