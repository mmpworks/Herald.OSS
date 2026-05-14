#nullable enable

using MMP.Herald.Configuration;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Pipeline;
using MMP.Herald.Routing;

namespace MMP.Herald.Quick;

// Scalar mutations, collection convenience shortcuts, and Reset.
// Full collection CRUD lives on the respective Set classes.
public sealed partial class QuickLogBuilder
{
    // -- File sink updates --

    /// <summary>Update the file rolling policy. File sink must already be configured.</summary>
    public QuickLogBuilder UpdateFileRollingPolicy(FileSinkPolicy policy) {
        if (_logFilePath is null)
            throw new System.InvalidOperationException("No file sink configured. Call WithFileSink() first.");

        _logFileRolling = new JsonFileRollingConfig(
            Interval: policy.Interval, MaxBytes: policy.MaxBytes,
            MaxRetainedFiles: policy.MaxRetainedFiles, LogQueueSize: policy.LogQueueSize,
            StartMinute: policy.StartMinute, CaptureDurationMinutes: policy.CaptureDurationMinutes,
            FileNameSuffix: policy.FileNameSuffix, Locale: policy.Locale,
            RetentionDays: policy.RetentionDays, TotalSizeCapBytes: policy.TotalSizeCapBytes);
        return this;
    }

    /// <summary>Update the console sink's minimum level without changing the writer.</summary>
    public QuickLogBuilder UpdateConsoleMinLevel(string? minLevel) {
        if (!_includeConsole)
            throw new System.InvalidOperationException("No console sink configured. Call WithConsoleSink() first.");
        _consoleMinLevel = minLevel;
        return this;
    }

    /// <summary>Update only the file sink's minimum level.</summary>
    public QuickLogBuilder UpdateFileMinLevel(string? minLevel) {
        if (_logFilePath is null)
            throw new System.InvalidOperationException("No file sink configured. Call WithFileSink() first.");
        _logFileMinLevel = minLevel;
        return this;
    }

    // -- Scalar removals --

    /// <summary>Remove the console sink.</summary>
    public QuickLogBuilder WithoutConsoleSink() {
        _includeConsole = false; _consoleWriter = null; _consoleMinLevel = null;
        return this;
    }

    /// <summary>Remove the file sink.</summary>
    public QuickLogBuilder WithoutFileSink() {
        _logFilePath = null; _logFileMinLevel = null; _logFileRolling = null;
        return this;
    }

    /// <summary>Disable trace correlation.</summary>
    public QuickLogBuilder WithoutTraceCorrelation() { _includeActivityContext = false; return this; }

    /// <summary>Remove the global signal handler.</summary>
    public QuickLogBuilder WithoutSignalHandler() { _globalSignalHandler = null; return this; }

    /// <summary>Disable level dump at startup.</summary>
    public QuickLogBuilder WithoutLevelDump() { _dumpLevels = false; return this; }

    /// <summary>Remove the custom time provider (revert to system clock).</summary>
    public QuickLogBuilder WithoutTimeProvider() { _timeProvider = null; return this; }

    // -- Convenience: Collection removals (delegate to Set classes) --

    /// <summary>Convenience: remove a property style by name.</summary>
    public QuickLogBuilder WithoutPropertyStyle(string propertyName) =>
        PropertyStyles.Remove(propertyName);

    /// <summary>Convenience: remove a category (channel) style by name.</summary>
    public QuickLogBuilder WithoutCategoryStyle(string categoryName) =>
        CategoryStyles.Remove(categoryName);

    /// <summary>Convenience: remove a channel by name.</summary>
    public QuickLogBuilder WithoutChannel(string channelName) =>
        Channels.Remove(channelName);

    /// <summary>Convenience: remove a named event processor.</summary>
    public QuickLogBuilder WithoutEventProcessor(string name) =>
        EventProcessors.Remove(name);

    /// <summary>Convenience: remove all non-protected processors of a specific type.</summary>
    public QuickLogBuilder WithoutProcessorsOfType<T>() where T : ILogEventProcessor =>
        EventProcessors.RemoveOfType<T>();

    /// <summary>Convenience: remove a custom sink provider by kind.</summary>
    public QuickLogBuilder WithoutCustomSinkProvider(string sinkKind) =>
        SinkProviders.Remove(sinkKind);

    // -- Convenience: Collection clears (delegate to Set classes) --

    /// <summary>Convenience: remove all channels.</summary>
    public QuickLogBuilder ClearChannels() =>
        Channels.Clear();

    /// <summary>Convenience: remove all audit sinks.</summary>
    public QuickLogBuilder ClearAuditSinks() =>
        AuditSinks.Clear();

    /// <summary>Convenience: remove all custom property styles.</summary>
    public QuickLogBuilder ClearPropertyStyles() =>
        PropertyStyles.Clear();

    /// <summary>Convenience: remove all custom category (channel) styles.</summary>
    public QuickLogBuilder ClearCategoryStyles() =>
        CategoryStyles.Clear();

    /// <summary>Convenience: remove all non-protected event processors.</summary>
    public QuickLogBuilder ClearEventProcessors() =>
        EventProcessors.Clear();

    // -- Convenience: Collection queries (delegate to Set classes) --

    /// <summary>Convenience: get a named event processor.</summary>
    public T? GetEventProcessor<T>(string name) where T : class, ILogEventProcessor =>
        EventProcessors.Get<T>(name);

    /// <summary>Convenience: get an event processor by type (first match).</summary>
    public T? GetEventProcessor<T>() where T : class, ILogEventProcessor =>
        EventProcessors.Get<T>();

    /// <summary>Convenience: get a sink provider by kind.</summary>
    public ILogSinkProvider? GetSinkProvider(string sinkKind) =>
        SinkProviders.Get(sinkKind);

    // -- Convenience: Collection replacements (delegate to Set classes) --

    /// <summary>Convenience: replace a named event processor.</summary>
    public bool ReplaceEventProcessor(string name, ILogEventProcessor newProcessor) =>
        EventProcessors.Replace(name, newProcessor);

    /// <summary>Convenience: replace or add a property style.</summary>
    public QuickLogBuilder SetPropertyStyle(string propertyName, string colorName,
        bool bold = false, bool italic = false, string? backgroundColorName = null) =>
        PropertyStyles.Set(propertyName, colorName, bold, italic, backgroundColorName);

    /// <summary>Convenience: replace or add a category (channel) style.</summary>
    public QuickLogBuilder SetCategoryStyle(string categoryName, string? colorName = null,
        bool bold = false, bool italic = false, string? backgroundColorName = null) =>
        CategoryStyles.Set(categoryName, colorName, bold, italic, backgroundColorName);

    // -- Reset --

    /// <summary>
    /// Reset to initial state. Protected registrations survive.
    /// </summary>
    public QuickLogBuilder Reset() {
        _minimumLevel = Services.LogLevelKeys.Info;
        _includeConsole = false;
        _consoleMinLevel = null;
        _consoleWriter = null;
        _dumpLevels = false;
        _logFilePath = null;
        _logFileMinLevel = null;
        _logFileRolling = null;
        Enrichers.Reset();
        EventProcessors.Clear();
        PropertyStyles.Clear();
        CategoryStyles.Clear();
        SinkProviders.Clear();
        Channels.Clear();
        AuditSinks.Clear();
        Bridges.Clear();
        _includeActivityContext = false;
        _globalSignalHandler = null;
        _timeProvider = null;
        _levelStyleOverrides?.Clear();
        return this;
    }
}
