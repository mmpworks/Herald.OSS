#nullable enable

namespace MMP.Herald.Services;

/// <summary>
/// Single entry point for all Herald constants.
/// Use this instead of remembering individual constant class names.
///
/// Usage:
///   builder.WithMinimumLevel(Known.Levels.Debug)
///   builder.WithPropertyStyle("name", Known.Colors.Cyan, bold: true)
///   builder.WithFileSink("logs/game.log", interval: Known.Json.IntervalDaily)
///
/// Each nested class delegates to the underlying constant class, which
/// can also be used directly (LogLevelKeys, KnownAnsiColors, etc.).
/// </summary>
public static class Known
{
    /// <summary>Log level key strings: Trace, Debug, Info, Warn, Error, Critical, Fatal, etc.</summary>
    public static class Levels
    {
        public const string Trace = LogLevelKeys.Trace;
        public const string Debug = LogLevelKeys.Debug;
        public const string Info = LogLevelKeys.Info;
        public const string Notice = LogLevelKeys.Notice;
        public const string Success = LogLevelKeys.Success;
        public const string Warn = LogLevelKeys.Warn;
        public const string Error = LogLevelKeys.Error;
        public const string Critical = LogLevelKeys.Critical;
        public const string Security = LogLevelKeys.Security;
        public const string Metric = LogLevelKeys.Metric;
        public const string Fatal = LogLevelKeys.Fatal;
    }

    /// <summary>ANSI color names: Red, Green, Cyan, Gold, SkyBlue, DimGray, etc.</summary>
    public static class Colors
    {
        public const string Black = KnownAnsiColors.Black;
        public const string Red = KnownAnsiColors.Red;
        public const string Green = KnownAnsiColors.Green;
        public const string Yellow = KnownAnsiColors.Yellow;
        public const string Blue = KnownAnsiColors.Blue;
        public const string Magenta = KnownAnsiColors.Magenta;
        public const string Cyan = KnownAnsiColors.Cyan;
        public const string White = KnownAnsiColors.White;
        public const string Gray = KnownAnsiColors.Gray;
        public const string DimGray = KnownAnsiColors.DimGray;
        public const string DarkGray = KnownAnsiColors.DarkGray;
        public const string LightGray = KnownAnsiColors.LightGray;
        public const string Orange = KnownAnsiColors.Orange;
        public const string Pink = KnownAnsiColors.Pink;
        public const string Purple = KnownAnsiColors.Purple;
        public const string Brown = KnownAnsiColors.Brown;
        public const string Lime = KnownAnsiColors.Lime;
        public const string Teal = KnownAnsiColors.Teal;
        public const string Gold = KnownAnsiColors.Gold;
        public const string Coral = KnownAnsiColors.Coral;
        public const string Salmon = KnownAnsiColors.Salmon;
        public const string SkyBlue = KnownAnsiColors.SkyBlue;
        public const string Olive = KnownAnsiColors.Olive;
        public const string Crimson = KnownAnsiColors.Crimson;
        public const string Slate = KnownAnsiColors.Slate;
        public const string Indigo = KnownAnsiColors.Indigo;
    }

    /// <summary>Sink kind identifiers: Console, TextFile, JsonFile, HttpJson, etc.</summary>
    public static class Sinks
    {
        public const string Console = KnownSinkKinds.Console;
        public const string TextFile = KnownSinkKinds.TextFile;
        public const string JsonFile = KnownSinkKinds.JsonFile;
        public const string HttpJson = KnownSinkKinds.HttpJson;
        public const string TcpJsonLine = KnownSinkKinds.TcpJsonLine;
        public const string OtlpJson = KnownSinkKinds.OtlpJson;
        public const string OtlpProtobuf = KnownSinkKinds.OtlpProtobuf;
        public const string ProtobufFile = KnownSinkKinds.ProtobufFile;
        public const string PipelineBridge = KnownSinkKinds.PipelineBridge;
    }

    /// <summary>Async queue drop strategies: DropWrite, DropOldest, Wait.</summary>
    public static class DropStrategies
    {
        public const string DropWrite = KnownDropStrategies.DropWrite;
        public const string DropOldest = KnownDropStrategies.DropOldest;
        public const string Wait = KnownDropStrategies.Wait;
    }

    /// <summary>Built-in context key names: Channel, Audit, TraceId, SpanId, etc.</summary>
    public static class Context
    {
        public const string Channel = LogContextKeys.Channel;
        public const string Audit = LogContextKeys.Audit;
        public const string Exception = LogContextKeys.Exception;
        public const string TraceId = LogContextKeys.TraceId;
        public const string SpanId = LogContextKeys.SpanId;
        public const string SequenceNumber = LogContextKeys.SequenceNumber;
    }

    /// <summary>Named event processor registration names.</summary>
    public static class Processors
    {
        public const string CompiledRedaction = KnownProcessorNames.CompiledRedaction;
        public const string LogDeduplication = KnownProcessorNames.LogDeduplication;
        public const string AdaptiveSampling = KnownProcessorNames.AdaptiveSampling;
        public const string MetricExtraction = KnownProcessorNames.MetricExtraction;
    }

    /// <summary>Well-known property names for structured log templates and property styles.</summary>
    public static class Properties
    {
        public const string UserId = KnownPropertyNames.UserId;
        public const string UserName = KnownPropertyNames.UserName;
        public const string EntityId = KnownPropertyNames.EntityId;
        public const string EntityName = KnownPropertyNames.EntityName;
        public const string Action = KnownPropertyNames.Action;
        public const string Operation = KnownPropertyNames.Operation;
        public const string Status = KnownPropertyNames.Status;
        public const string Result = KnownPropertyNames.Result;
        public const string Value = KnownPropertyNames.Value;
        public const string Delta = KnownPropertyNames.Delta;
        public const string Count = KnownPropertyNames.Count;
        public const string Amount = KnownPropertyNames.Amount;
        public const string Duration = KnownPropertyNames.Duration;
        public const string Elapsed = KnownPropertyNames.Elapsed;
        public const string Path = KnownPropertyNames.Path;
        public const string Source = KnownPropertyNames.Source;
        public const string Target = KnownPropertyNames.Target;
        public const string Endpoint = KnownPropertyNames.Endpoint;
        public const string Error = KnownPropertyNames.Error;
        public const string Reason = KnownPropertyNames.Reason;
        public const string Exception = KnownPropertyNames.Exception;
        public const string StackTrace = KnownPropertyNames.StackTrace;
        public const string Category = KnownPropertyNames.Category;
        public const string Type = KnownPropertyNames.Type;
        public const string Kind = KnownPropertyNames.Kind;
        public const string Tag = KnownPropertyNames.Tag;
        public const string Timestamp = KnownPropertyNames.Timestamp;
        public const string TimeOfDay = KnownPropertyNames.TimeOfDay;
    }

    /// <summary>Pipeline default values: BatchSize, AsyncCapacity, etc.</summary>
    public static class Defaults
    {
        public const int BatchSize = PipelineDefaults.BatchSize;
        public const int BatchDelayMs = PipelineDefaults.BatchDelayMs;
        public const int AsyncCapacity = PipelineDefaults.AsyncCapacity;
        public const long WalMaxBytes = PipelineDefaults.WalMaxBytes;
    }

    /// <summary>JSON config property names and value constants.</summary>
    public static class Json
    {
        public const string IntervalDaily = JsonConfigProperties.IntervalDaily;
        public const string IntervalHourly = JsonConfigProperties.IntervalHourly;
        public const string IntervalCustom = JsonConfigProperties.IntervalCustom;
        public const string IntervalNone = JsonConfigProperties.IntervalNone;
        public const string ThemeDark = JsonConfigProperties.ThemeDark;
        public const string ThemeLight = JsonConfigProperties.ThemeLight;
        public const string ThemeLiterate = JsonConfigProperties.ThemeLiterate;
        public const string ThemeNone = JsonConfigProperties.ThemeNone;
    }
}
