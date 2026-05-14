#nullable enable

namespace MMP.Herald.Events;
/// <summary>
/// Small domain value for logger categories.
/// </summary>
public sealed record LogCategory(string Value)
{
    /// <summary>
    /// Sentinel for callers who do not want to label a log call with a
    /// category. Used by the typed-args overloads on <c>StructuredLogger</c>
    /// (e.g. <c>Debug&lt;T1&gt;(string, T1)</c>) so the call site matches the
    /// shape Serilog and NLog expose. Sinks see this as an empty-string
    /// category, which routing rules can match if desired.
    /// </summary>
    public static LogCategory None { get; } = new(string.Empty);

    public static LogCategory App { get; } = new("App");
    public static LogCategory Startup { get; } = new("Startup");
    public static LogCategory Input { get; } = new("Input");
    public static LogCategory Camera { get; } = new("Camera");
    public static LogCategory Board { get; } = new("Board");
    public static LogCategory Ui { get; } = new("Ui");

    public override string ToString()
    {
        return Value;
    }
}