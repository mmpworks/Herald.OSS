#nullable enable

using System;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog;

/// <summary>
/// No-op <see cref="ILogger"/> used as the defensive default for
/// <see cref="Log.Logger"/> before a real logger is assigned.
/// Every method is a no-op. <see cref="IsEnabled"/> always returns <c>false</c>
/// so callers that guard expensive property construction skip the work.
/// </summary>
internal sealed class SilentLogger : ILogger
{
    /// <summary>Singleton — no state, no allocation per access.</summary>
    public static readonly SilentLogger Instance = new();

    private SilentLogger() { }

    public bool IsEnabled(LogEventLevel level) => false;

    public void Write(LogEventLevel level, string messageTemplate, params object?[]? propertyValues) { }
    public void Write(LogEventLevel level, Exception? exception, string messageTemplate, params object?[]? propertyValues) { }

    public void Verbose(string messageTemplate, params object?[]? propertyValues) { }
    public void Debug(string messageTemplate, params object?[]? propertyValues) { }
    public void Information(string messageTemplate, params object?[]? propertyValues) { }
    public void Warning(string messageTemplate, params object?[]? propertyValues) { }
    public void Error(string messageTemplate, params object?[]? propertyValues) { }
    public void Fatal(string messageTemplate, params object?[]? propertyValues) { }

    public void Verbose(Exception? exception, string messageTemplate, params object?[]? propertyValues) { }
    public void Debug(Exception? exception, string messageTemplate, params object?[]? propertyValues) { }
    public void Information(Exception? exception, string messageTemplate, params object?[]? propertyValues) { }
    public void Warning(Exception? exception, string messageTemplate, params object?[]? propertyValues) { }
    public void Error(Exception? exception, string messageTemplate, params object?[]? propertyValues) { }
    public void Fatal(Exception? exception, string messageTemplate, params object?[]? propertyValues) { }

    public ILogger ForContext(string propertyName, object? value, bool destructureObjects = false) => this;
    public ILogger ForContext<TSource>() => this;
    public ILogger ForContext(Type source) => this;
}
