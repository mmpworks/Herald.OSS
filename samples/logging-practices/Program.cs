// Twelve logging practices, one small file each. Run all twelve:
//   dotnet run
// Run one practice by number:
//   dotnet run -- 9
using LoggingPractices;
using LoggingPractices.Practices;

var practices = new (int Number, string Title, Func<Task> Run)[]
{
    (1,  "Clear objectives",                         ClearObjectives.RunAsync),
    (2,  "Log levels and adjustable verbosity",      LevelsAndVerbosity.RunAsync),
    (3,  "Structured logging",                       StructuredLogging.RunAsync),
    (4,  "Context in every entry",                   ContextInEveryEntry.RunAsync),
    (5,  "Log sampling",                             LogSampling.RunAsync),
    (6,  "Canonical log lines and tracing",          CanonicalLogLines.RunAsync),
    (7,  "Aggregate and centralize",                 AggregateAndCentralize.RunAsync),
    (8,  "Retention policies",                       RetentionPolicies.RunAsync),
    (9,  "Secure your logs",                         SecureYourLogs.RunAsync),
    (10, "Keep sensitive data out",                  KeepSensitiveDataOut.RunAsync),
    (11, "Performance impact",                       PerformanceImpact.RunAsync),
    (12, "Logs are not metrics",                     LogsAreNotMetrics.RunAsync),
};

var only = args.Length > 0 && int.TryParse(args[0], out var n) ? n : (int?)null;

foreach (var (number, title, run) in practices)
{
    if (only is not null && only != number) continue;

    Console.WriteLine();
    Console.WriteLine($"=== {number}. {title} ===");
    await run();
}
