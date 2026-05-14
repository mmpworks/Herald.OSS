#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using MMP.Herald.Configuration;

namespace MMP.Herald.Quick;

// Inspection, validation, and export methods.
public sealed partial class QuickLogBuilder
{
    /// <summary>
    /// Returns an immutable snapshot of the builder's current configuration.
    /// </summary>
    public BuilderInspection Inspect() {
        return new BuilderInspection(
            MinimumLevel: _minimumLevel,
            HasConsoleSink: _includeConsole,
            ConsoleMinLevel: _consoleMinLevel,
            HasConsoleWriter: _consoleWriter is not null,
            HasNullSink: _includeNullSink,
            NullSinkMinLevel: _nullSinkMinLevel,
            HasFileSink: _logFilePath is not null,
            FilePath: _logFilePath,
            FileMinLevel: _logFileMinLevel,
            FileKind: _logFileKind,
            HasFileRolling: _logFileRolling is not null,
            FileRollingInterval: _logFileRolling?.Interval,
            FileMaxBytes: _logFileRolling?.MaxBytes,
            FileMaxRetainedFiles: _logFileRolling?.MaxRetainedFiles,
            FileNamePattern: _logFileRolling?.FileNameSuffix,
            ChannelNames: Channels.Items.ConvertAll(static c => c.Name),
            PropertyStyles: PropertyStyles.Items.ConvertAll(static s =>
                new PropertyStyleInfo(s.PropertyName, s.ColorName, s.UseBold, s.UseItalic, s.BackgroundColorName)),
            EventProcessors: EventProcessors.Items.ConvertAll(static r =>
                new ProcessorInfo(r.Name, r.Value.GetType(), r.IsProtected)),
            CustomSinkProviderKinds: SinkProviders.Items.ConvertAll(static r => r.Name),
            AuditSinkCount: AuditSinks.Items.Count,
            BridgeCount: Bridges.Items.Count,
            IncludeActivityContext: _includeActivityContext,
            HasSignalHandler: _globalSignalHandler is not null,
            HasCustomTimeProvider: _timeProvider is not null,
            DumpLevels: _dumpLevels,
            RetentionDays: _logFileRolling?.RetentionDays,
            TotalSizeCapBytes: _logFileRolling?.TotalSizeCapBytes,
            PipelineStrategy: _pipelineStrategy,
            LevelStyles: BuildLevelStyles().ConvertAll(static s =>
                new LevelStyleInfo(s.LevelKey, s.ColorName, s.UseBold, s.UseItalic, s.BackgroundColorName)),
            CategoryStyles: CategoryStyles.Items.ConvertAll(static s =>
                new CategoryStyleInfo(s.CategoryName, s.ColorName, s.UseBold, s.UseItalic, s.BackgroundColorName)),
            DeferRendering: _asyncDeferRendering,
            AsyncEnabled: _asyncEnabled,
            AsyncCapacity: _asyncCapacity,
            AsyncDropStrategy: _asyncDropStrategy,
            BatchingEnabled: _batchingEnabled,
            BatchMaxSize: _batchMaxSize,
            BatchMaxDelayMs: _batchDelayMs,
            SamplingRate: _samplingRate,
            FlightRecorderEnabled: _flightRecorderEnabled,
            FlightRecorderBufferSize: _flightRecorderBufferSize,
            FlightRecorderMinLevel: _flightRecorderMinLevel,
            FlightRecorderTriggerLevel: _flightRecorderTriggerLevel,
            PostFilteringEnabled: _postFilteringEnabled,
            PostFilteringMaxBatchSize: _postFilteringMaxBatchSize,
            PostFilteringMaxBatchDelayMs: _postFilteringMaxBatchDelayMs);
    }

    /// <summary>
    /// Validate the builder configuration. Returns issues without throwing.
    /// Delegates to QuickLogBuilderValidator for testability and SRP.
    /// </summary>
    public ValidationResult Validate() =>
        QuickLogBuilderValidator.Validate(Inspect(), filePath: _logFilePath);

    /// <summary>
    /// Export the builder's current configuration as a JSON string.
    /// Uses Build() internally to produce the exact same JSON that would be committed.
    /// </summary>
    public string ExportConfig() {
        var buildResult = Build();
        return buildResult.ExportConfig();
    }

    /// <summary>
    /// Export the builder's current configuration directly to a JSON file.
    /// Creates the directory if it doesn't exist.
    /// </summary>
    public void ExportConfigToFile(string filePath) {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var directory = System.IO.Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        var json = ExportConfig();
        System.IO.File.WriteAllText(filePath, json);
    }
}
