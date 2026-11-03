# Logging practices sample

One console app. Twelve small files. Each file shows one logging practice with
the Herald API that covers it, and prints what that API did.

## Run it

```bash
cd samples/logging-practices
dotnet run -c Release          # all twelve practices
dotnet run -c Release -- 9     # one practice, by number
```

The project builds Herald.OSS from source through a project reference. It needs
the .NET 10 SDK. It adds no NuGet packages of its own.

## The files

| # | Practice | File | Herald API |
|---|----------|------|------------|
| 1 | Clear objectives | `Practices/01-ClearObjectives.cs` | `LogEventId`, `LogCategory` |
| 2 | Log levels and adjustable verbosity | `Practices/02-LevelsAndVerbosity.cs` | `LogLevelSwitch`, `WithFastDynamicLevel` (`FastPathDynamicLevel`) |
| 3 | Structured logging | `Practices/03-StructuredLogging.cs` | typed template arguments, JSON file sink |
| 4 | Context in every entry | `Practices/04-ContextInEveryEntry.cs` | `ActivityEnricher`, `TraceContextPropagator` |
| 5 | Log sampling | `Practices/05-LogSampling.cs` | `AdaptiveSamplingFilter`, `PriorityWindowSamplingFilter`, `FastPathSampler` |
| 6 | Canonical log lines and tracing | `Practices/06-CanonicalLogLines.cs` | `UseSerilogRequestLogging` (`RequestLoggingMiddleware`), `SpanScope` |
| 7 | Aggregate and centralize | `Practices/07-AggregateAndCentralize.cs` | `WithOtlpJsonSink`, `OtelLogRecord` |
| 8 | Retention policies | `Practices/08-RetentionPolicies.cs` | `FileSinkPolicy` |
| 9 | Secure your logs | `Practices/09-SecureYourLogs.cs` | `HmacChainLogger` |
| 10 | Keep sensitive data out | `Practices/10-KeepSensitiveDataOut.cs` | `CompiledRedactionProcessor`, `FastPathRedactor`, `LogPropertyVisibility.PiiSensitive` |
| 11 | Performance impact | `Practices/11-PerformanceImpact.cs` | null sink, typed call shape |
| 12 | Logs are not metrics | `Practices/12-LogsAreNotMetrics.cs` | `LogMetricExtractor` |

`CaptureLogger.cs` is a small `ILogger` the samples attach with `WithBridge(...)`.
It prints the fields a sink receives, so you can see properties and context
beside the rendered message.

## Notes on two practices

- **Practice 7** runs without an OpenTelemetry collector. It prints the OTLP
  `LogRecord` that `OtelLogRecord` builds from a Herald event. To ship logs, add
  the `MMP.Herald.Sinks.Otlp` package and call
  `WithOtlpJsonSink("http://collector:4318/v1/logs")`.
- **Practice 11** measures one run on your machine. The published per-call and
  soak numbers, with their harness, are in `docs/benchmarks/`.
