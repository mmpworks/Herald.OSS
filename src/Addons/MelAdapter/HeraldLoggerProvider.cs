#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Templating;
using MEL = Microsoft.Extensions.Logging;

namespace MMP.Herald.Addons.MelAdapter;

/// <summary>
/// Bridges Microsoft.Extensions.Logging (MEL) to Herald's StructuredLogger.
/// Libraries that accept MEL's ILogger&lt;T&gt; can log through Herald's pipeline
/// without knowing Herald exists.
///
/// Usage:
///   var result = QuickLogBuilder.Create().WithConsoleSink().Build();
///
///   // Create the MEL provider backed by Herald:
///   var provider = new HeraldLoggerProvider(result.Logger);
///
///   // Use in DI:
///   services.AddSingleton&lt;ILoggerProvider&gt;(provider);
///
///   // Or create loggers directly:
///   ILogger&lt;MyService&gt; melLogger = provider.CreateLogger&lt;MyService&gt;();
///   melLogger.LogInformation("Player {Name} joined", playerName);
///   // → flows through Herald's full pipeline (enrichers, sinks, etc.)
/// </summary>
public sealed class HeraldLoggerProvider : MEL.ILoggerProvider
{
    private readonly StructuredLogger _heraldLogger;
    private readonly ConcurrentDictionary<string, HeraldMelLogger> _loggers = new();

    public HeraldLoggerProvider(StructuredLogger heraldLogger)
    {
        _heraldLogger = heraldLogger ?? throw new ArgumentNullException(nameof(heraldLogger));
    }

    public MEL.ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new HeraldMelLogger(_heraldLogger, name));

    /// <summary>Create a typed MEL logger backed by Herald.</summary>
    public MEL.ILogger<T> CreateLogger<T>() =>
        new TypedHeraldMelLogger<T>(CreateLogger(typeof(T).FullName ?? typeof(T).Name));

    public void Dispose() => _loggers.Clear();
}

/// <summary>
/// MEL ILogger implementation that delegates to Herald's StructuredLogger.
/// Maps MEL log levels to Herald levels, MEL state to Herald properties,
/// and MEL scopes to Herald scopes.
/// </summary>
internal sealed class HeraldMelLogger : MEL.ILogger
{
    private readonly StructuredLogger _heraldLogger;
    private readonly LogCategory _category;

    public HeraldMelLogger(StructuredLogger heraldLogger, string categoryName)
    {
        _heraldLogger = heraldLogger;
        _category = new LogCategory(categoryName);
    }

    public bool IsEnabled(MEL.LogLevel logLevel)
    {
        if (logLevel == MEL.LogLevel.None) return false;
        return _heraldLogger.IsEnabled(MapLevel(logLevel));
    }

    public void Log<TState>(
        MEL.LogLevel logLevel,
        MEL.EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var heraldLevel = MapLevel(logLevel);
        var message = formatter(state, exception);

        IReadOnlyDictionary<string, object?>? context = null;
        if (exception is not null)
        {
            context = new Dictionary<string, object?>
            {
                [Services.LogContextKeys.Exception] = exception
            };
        }

        LogEventId? heraldEventId = eventId.Id != 0
            ? new LogEventId(eventId.Id, eventId.Name)
            : null;

        // Extract structured properties from MEL state if available
        IReadOnlyList<LogProperty>? properties = null;
        if (state is IReadOnlyList<KeyValuePair<string, object?>> stateProperties)
        {
            var props = new List<LogProperty>(stateProperties.Count);
            foreach (var kvp in stateProperties)
            {
                // MEL uses "{OriginalFormat}" for the template -- skip it
                if (kvp.Key == "{OriginalFormat}") continue;
                props.Add(new LogProperty(kvp.Key, kvp.Value));
            }
            if (props.Count > 0) properties = props;
        }

        _heraldLogger.Log(heraldLevel, _category, message, properties, context, heraldEventId);
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        if (state is IReadOnlyDictionary<string, object?> dict)
        {
            return _heraldLogger.BeginScope(dict);
        }

        // Wrap non-dictionary state as a single "scope" entry
        return _heraldLogger.BeginScope(new Dictionary<string, object?>
        {
            ["scope"] = state
        });
    }

    private static LogLevel MapLevel(MEL.LogLevel melLevel) => melLevel switch
    {
        MEL.LogLevel.Trace => KnownLogLevels.Trace,
        MEL.LogLevel.Debug => KnownLogLevels.Debug,
        MEL.LogLevel.Information => KnownLogLevels.Info,
        MEL.LogLevel.Warning => KnownLogLevels.Warn,
        MEL.LogLevel.Error => KnownLogLevels.Error,
        MEL.LogLevel.Critical => KnownLogLevels.Critical,
        _ => KnownLogLevels.Info
    };
}

/// <summary>Typed MEL logger wrapper for DI compatibility.</summary>
internal sealed class TypedHeraldMelLogger<T> : MEL.ILogger<T>
{
    private readonly MEL.ILogger _inner;

    public TypedHeraldMelLogger(MEL.ILogger inner) => _inner = inner;

    public bool IsEnabled(MEL.LogLevel logLevel) => _inner.IsEnabled(logLevel);

    public void Log<TState>(MEL.LogLevel logLevel, MEL.EventId eventId,
        TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        _inner.Log(logLevel, eventId, state, exception, formatter);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
        _inner.BeginScope(state);
}
