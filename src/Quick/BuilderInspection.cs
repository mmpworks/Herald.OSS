#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using MMP.Herald.Pipeline;

namespace MMP.Herald.Quick;

/// <summary>
/// Immutable snapshot of a QuickLogBuilder's current configuration.
/// Returned by QuickLogBuilder.Inspect() for full CRUD visibility.
/// </summary>
public sealed record BuilderInspection(
    string MinimumLevel,
    bool HasConsoleSink,
    string? ConsoleMinLevel,
    bool HasConsoleWriter,
    bool HasNullSink,
    string? NullSinkMinLevel,
    bool HasFileSink,
    string? FilePath,
    string? FileMinLevel,
    string? FileKind,
    bool HasFileRolling,
    string? FileRollingInterval,
    long? FileMaxBytes,
    int? FileMaxRetainedFiles,
    string? FileNamePattern,
    IReadOnlyList<string> ChannelNames,
    IReadOnlyList<PropertyStyleInfo> PropertyStyles,
    IReadOnlyList<ProcessorInfo> EventProcessors,
    IReadOnlyList<string> CustomSinkProviderKinds,
    int AuditSinkCount,
    int BridgeCount,
    bool IncludeActivityContext,
    bool HasSignalHandler,
    bool HasCustomTimeProvider,
    bool DumpLevels,
    int? RetentionDays = null,
    long? TotalSizeCapBytes = null,
    Configuration.PipelineStrategy? PipelineStrategy = null,
    IReadOnlyList<LevelStyleInfo>? LevelStyles = null,
    IReadOnlyList<CategoryStyleInfo>? CategoryStyles = null,
    bool DeferRendering = false,
    // Per-step pipeline policy snapshot. Surfaced on the inspection so the
    // management API's Set*Config methods (and their tests) can read the
    // current values back without reaching into the builder's private
    // fields. Mirrors the CategoryStyles round-trip pattern.
    bool AsyncEnabled = false,
    int AsyncCapacity = 0,
    string? AsyncDropStrategy = null,
    bool BatchingEnabled = false,
    int BatchMaxSize = 0,
    int BatchMaxDelayMs = 0,
    int SamplingRate = 0,
    bool FlightRecorderEnabled = false,
    int FlightRecorderBufferSize = 0,
    string? FlightRecorderMinLevel = null,
    string? FlightRecorderTriggerLevel = null,
    bool PostFilteringEnabled = false,
    int PostFilteringMaxBatchSize = 0,
    int PostFilteringMaxBatchDelayMs = 0)
{
    // -- Computed properties --

    /// <summary>
    /// Total sinks: console + file + channels + audit + custom providers + bridges.
    /// <para>
    /// Network sinks (http_json, tcp_json_line, OTLP, elasticsearch, slack,
    /// webhook) are deliberately not counted here. They always pair with a
    /// primary sink — the validator rejects a pipeline whose only sinks are
    /// network — so this count stays focused on the "primary sink" surface
    /// that callers typically ask about. To enumerate network sinks, read
    /// them from the exported JSON config instead.
    /// </para>
    /// </summary>
    public int TotalSinkCount =>
        (HasConsoleSink ? 1 : 0) +
        (HasNullSink ? 1 : 0) +
        (HasFileSink ? 1 : 0) +
        ChannelNames.Count +
        AuditSinkCount +
        BridgeCount +
        CustomSinkProviderKinds.Count;

    /// <summary>True if any sink is configured.</summary>
    public bool HasAnySink => TotalSinkCount > 0;

    public int PropertyStyleCount => PropertyStyles.Count;
    public int CategoryStyleCount => CategoryStyles?.Count ?? 0;
    public int EventProcessorCount => EventProcessors.Count;
    public int CustomSinkProviderCount => CustomSinkProviderKinds.Count;

    // -- Query helpers --

    /// <summary>Check if a property style exists.</summary>
    public bool HasPropertyStyle(string propertyName) =>
        PropertyStyles.Any(s => string.Equals(s.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Get style info for a property, or null.</summary>
    public PropertyStyleInfo? GetPropertyStyle(string propertyName) =>
        PropertyStyles.FirstOrDefault(s => string.Equals(s.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Check if a category style exists.</summary>
    public bool HasCategoryStyle(string categoryName) =>
        CategoryStyles is not null && CategoryStyles.Any(s => string.Equals(s.CategoryName, categoryName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Get style info for a category, or null.</summary>
    public CategoryStyleInfo? GetCategoryStyle(string categoryName) =>
        CategoryStyles?.FirstOrDefault(s => string.Equals(s.CategoryName, categoryName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Check if a channel is configured.</summary>
    public bool HasChannel(string channelName) =>
        ChannelNames.Any(n => string.Equals(n, channelName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Check if a processor of a specific type is registered.</summary>
    public bool HasProcessorOfType<T>() where T : ILogEventProcessor =>
        EventProcessors.Any(p => p.Type == typeof(T));

    /// <summary>Check if a named processor is registered.</summary>
    public bool HasProcessor(string name) =>
        EventProcessors.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Check if a custom sink provider is registered by kind.</summary>
    public bool HasSinkProvider(string sinkKind) =>
        CustomSinkProviderKinds.Any(k => string.Equals(k, sinkKind, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Property style details.</summary>
public sealed record PropertyStyleInfo(
    string PropertyName,
    string? ColorName,
    bool UseBold,
    bool UseItalic,
    string? BackgroundColorName);

/// <summary>Event processor registration details.</summary>
public sealed record ProcessorInfo(
    string Name,
    Type Type,
    bool IsProtected);

/// <summary>Level display style details.</summary>
public sealed record LevelStyleInfo(
    string LevelKey,
    string ColorName,
    bool UseBold,
    bool UseItalic,
    string? BackgroundColorName);

/// <summary>Category (channel) display style details.</summary>
public sealed record CategoryStyleInfo(
    string CategoryName,
    string? ColorName,
    bool UseBold,
    bool UseItalic,
    string? BackgroundColorName);
