#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Templating;

namespace MMP.Herald.Addons.GamePerformance;

/// <summary>
/// Wrapper that provides Trace/Debug methods marked with [Conditional("DEBUG")].
/// In release builds, these methods are completely eliminated by the compiler -
/// no string formatting, no method call overhead, no allocation whatsoever.
///
/// This is the pattern Unity developers use extensively and expect from any
/// game logging framework.
///
/// Usage:
///   var debug = new ConditionalDebugLogger(logger);
///   debug.Trace(LogCategory.App, "Detailed trace info: {value}", props);  // eliminated in release
///   debug.Debug(LogCategory.App, "Debug state: {state}", props);          // eliminated in release
///   logger.Info(LogCategory.App, "Always logged");                         // always present
/// </summary>
public sealed class ConditionalDebugLogger
{
    private readonly StructuredLogger _logger;

    public ConditionalDebugLogger(StructuredLogger logger) {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Log at Trace level. Compiled out entirely in release builds.
    /// </summary>
    [Conditional("DEBUG")]
    public void Trace(
        LogCategory category,
        string messageTemplate,
        IReadOnlyList<LogProperty>? properties = null) {
        _logger.Trace(category, messageTemplate, properties: properties);
    }

    /// <summary>
    /// Log at Debug level. Compiled out entirely in release builds.
    /// </summary>
    [Conditional("DEBUG")]
    public void Debug(
        LogCategory category,
        string messageTemplate,
        IReadOnlyList<LogProperty>? properties = null) {
        _logger.Debug(category, messageTemplate, properties: properties);
    }

    /// <summary>
    /// Log at Trace level with context. Compiled out entirely in release builds.
    /// </summary>
    [Conditional("DEBUG")]
    public void TraceWithContext(
        LogCategory category,
        string messageTemplate,
        IReadOnlyDictionary<string, object?> context,
        IReadOnlyList<LogProperty>? properties = null) {
        _logger.Trace(category, messageTemplate, context: context, properties: properties);
    }

    /// <summary>
    /// Log at Debug level with context. Compiled out entirely in release builds.
    /// </summary>
    [Conditional("DEBUG")]
    public void DebugWithContext(
        LogCategory category,
        string messageTemplate,
        IReadOnlyDictionary<string, object?> context,
        IReadOnlyList<LogProperty>? properties = null) {
        _logger.Debug(category, messageTemplate, context: context, properties: properties);
    }
}
