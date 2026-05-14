#nullable enable

using System;

namespace MMP.Herald.Addons.Instrumentation;

/// <summary>
/// Marks a method for automatic span instrumentation.
///
/// Currently a marker attribute for documentation and future source generator support.
/// Use with SpanScope.Begin() for runtime instrumentation today:
///
///   [Instrument]
///   public void ProcessOrder(int orderId) {
///       using var span = SpanScope.Begin(logger, spanFactory);
///       // ... method body ...
///   }
///
/// When a Herald source generator is available, this attribute will automatically
/// wrap the method body in a SpanScope without manual code.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class InstrumentAttribute : Attribute
{
    /// <summary>
    /// Custom span name. Defaults to the method name if null.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Whether to capture method parameters as span properties.
    /// </summary>
    public bool CaptureParameters { get; set; }

    /// <summary>
    /// Parameter names to exclude from capture (e.g., passwords).
    /// </summary>
    public string[]? Skip { get; set; }
}
