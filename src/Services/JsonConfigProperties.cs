#nullable enable

namespace MMP.Herald.Services;

/// <summary>
/// Well-known JSON property names used in Herald's configuration file schema.
/// Use these when building or parsing configuration programmatically.
/// </summary>
public static class JsonConfigProperties
{
    // -- Root properties --
    public const string MinimumLevel = "minimumLevel";
    public const string Levels = "levels";
    public const string Async = "async";
    public const string Batching = "batching";
    public const string DumpRegisteredLevelsToConsole = "dumpRegisteredLevelsToConsole";
    public const string Aliases = "aliases";
    public const string LevelStyles = "levelStyles";
    public const string Sinks = "sinks";
    public const string Routes = "routes";
    public const string IncludeCallerInfo = "includeCallerInfo";
    public const string IncludeActivityContext = "includeActivityContext";
    public const string DynamicLevels = "dynamicLevels";
    public const string Sampling = "sampling";
    public const string PostFiltering = "postFiltering";
    public const string HotReload = "hotReload";
    public const string PropertyStyles = "propertyStyles";
    public const string ConsoleTheme = "consoleTheme";

    // -- Levels --
    public const string BaseLevels = "baseLevels";
    public const string AdditionalLevels = "additionalLevels";
    public const string Placements = "placements";
    public const string Key = "key";
    public const string DisplayName = "displayName";

    // -- Async --
    public const string Enabled = "enabled";
    public const string Capacity = "capacity";
    public const string DropStrategy = "dropStrategy";

    // -- Batching --
    public const string MaxBatchSize = "maxBatchSize";
    public const string MaxBatchDelayMs = "maxBatchDelayMs";

    // -- Sinks --
    public const string Name = "name";
    public const string Kind = "kind";
    public const string Path = "path";
    public const string Alias = "alias";
    public const string Uri = "uri";
    public const string Host = "host";
    public const string Port = "port";
    public const string MinLevel = "minLevel";
    public const string OutputTemplate = "outputTemplate";
    public const string IsAudit = "isAudit";
    public const string EnableMetrics = "enableMetrics";

    // -- Retry --
    public const string Retry = "retry";
    public const string MaxAttempts = "maxAttempts";
    public const string DelayMs = "delayMs";

    // -- Rolling --
    public const string Rolling = "rolling";
    public const string Interval = "interval";
    public const string MaxBytes = "maxBytes";
    public const string MaxRetainedFiles = "maxRetainedFiles";
    public const string LogQueueSize = "logQueueSize";
    public const string StartMinute = "startMinute";
    public const string CaptureDurationMinutes = "captureDurationMinutes";
    public const string FileNameSuffix = "fileNameSuffix";
    public const string Locale = "locale";
    public const string RetentionDays = "retentionDays";
    public const string TotalSizeCapBytes = "totalSizeCapBytes";

    // -- Rolling intervals --
    public const string IntervalNone = "none";
    public const string IntervalHourly = "hourly";
    public const string IntervalDaily = "daily";
    public const string IntervalCustom = "custom";

    // -- Routes --
    public const string SinkName = "sinkName";
    public const string Predicate = "predicate";

    // -- Styles --
    public const string LevelKey = "levelKey";
    public const string ColorName = "colorName";
    public const string UseBold = "useBold";
    public const string UseItalic = "useItalic";
    public const string BackgroundColorName = "backgroundColorName";
    public const string PropertyName = "propertyName";

    // -- Theme --
    public const string Element = "element";
    public const string Qualifier = "qualifier";
    public const string ThemeName = "name";

    // -- Hot reload --
    public const string DebounceMs = "debounceMs";

    // -- Level placement positions --
    public const string PlacementBefore = "before";
    public const string PlacementAfter = "after";

    // -- Theme names --
    public const string ThemeDark = "dark";
    public const string ThemeLight = "light";
    public const string ThemeLiterate = "literate";
    public const string ThemeNone = "none";
}
