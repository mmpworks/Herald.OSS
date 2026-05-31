#nullable enable

using System;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog;

/// <summary>
/// Serilog-shaped application-facing logger interface. Mirrors the public
/// surface of <c>Serilog.ILogger</c> so existing Serilog call sites compile
/// against this shim without changes.
///
/// <para>
/// The <c>params object?[]?</c> overloads are the fallback path for callers
/// that do not have a source-generated typed-args overload (arity unknown
/// or arity &gt; 16). Task 4's generator emits zero-alloc typed overloads in
/// front of these — for now this fallback path is the only route and is
/// deliberately allocation-permissive.
/// </para>
/// </summary>
public interface ILogger
{
    /// <summary>Returns true when <paramref name="level"/> passes the pipeline floor.</summary>
    bool IsEnabled(LogEventLevel level);

    // -- Write overloads --

    void Write(LogEventLevel level, string messageTemplate, params object?[]? propertyValues);
    void Write(LogEventLevel level, Exception? exception, string messageTemplate, params object?[]? propertyValues);

    // -- Verb overloads (no exception) --

    void Verbose(string messageTemplate, params object?[]? propertyValues);
    void Debug(string messageTemplate, params object?[]? propertyValues);
    void Information(string messageTemplate, params object?[]? propertyValues);
    void Warning(string messageTemplate, params object?[]? propertyValues);
    void Error(string messageTemplate, params object?[]? propertyValues);
    void Fatal(string messageTemplate, params object?[]? propertyValues);

    // -- Verb overloads (with exception) --

    void Verbose(Exception? exception, string messageTemplate, params object?[]? propertyValues);
    void Debug(Exception? exception, string messageTemplate, params object?[]? propertyValues);
    void Information(Exception? exception, string messageTemplate, params object?[]? propertyValues);
    void Warning(Exception? exception, string messageTemplate, params object?[]? propertyValues);
    void Error(Exception? exception, string messageTemplate, params object?[]? propertyValues);
    void Fatal(Exception? exception, string messageTemplate, params object?[]? propertyValues);

    // -- Context --

    /// <summary>
    /// Return a new logger that attaches <paramref name="value"/> under
    /// <paramref name="propertyName"/> to every subsequent event.
    /// </summary>
    ILogger ForContext(string propertyName, object? value, bool destructureObjects = false);

    /// <summary>Return a new logger sourced from <typeparamref name="TSource"/>.</summary>
    ILogger ForContext<TSource>();

    /// <summary>Return a new logger sourced from <paramref name="source"/>.</summary>
    ILogger ForContext(Type source);
}
