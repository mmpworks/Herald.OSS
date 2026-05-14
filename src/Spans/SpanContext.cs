#nullable enable

using System;
using System.Threading;

namespace MMP.Herald.Spans;

/// <summary>
/// Tracks the current span via AsyncLocal so child spans automatically
/// inherit their parent without explicit wiring.
///
/// This enables automatic parent-child span propagation across async boundaries:
///
///   using var outer = spanFactory.Begin("LoadLevel");
///   // outer is now SpanContext.Current
///
///   await LoadAssetsAsync();
///   // inside LoadAssetsAsync:
///   using var inner = spanFactory.Begin("ParseMesh");
///   // inner automatically has outer as parent
///
/// Without SpanContext, you'd need to pass the parent span explicitly:
///   spanFactory.Begin("ParseMesh", parentSpan: outer);  // manual wiring
///
/// SpanContext eliminates this boilerplate while preserving explicit control
/// via LogSpan.BeginChild() for cases where you want to override the parent.
/// </summary>
public static class SpanContext
{
    private static readonly AsyncLocal<LogSpan?> _current = new();

    /// <summary>The currently active span, or null if no span is active.</summary>
    public static LogSpan? Current => _current.Value;

    /// <summary>
    /// Set the current span. Called by LogSpanFactory.Begin() and LogSpan.BeginChild().
    /// Returns the previous span so it can be restored on dispose.
    /// </summary>
    internal static LogSpan? SetCurrent(LogSpan? span)
    {
        var previous = _current.Value;
        _current.Value = span;
        return previous;
    }
}
