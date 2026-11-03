using MMP.Herald.Events;
using MMP.Herald.Quick;

namespace LoggingPractices.Practices;

/// <summary>
/// Practice 3. Log structured events. Every template hole becomes a named field,
/// and a .ndjson file path selects the built-in JSON file sink.
/// </summary>
public static class StructuredLogging
{
    public static async Task RunAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"herald-structured-{Environment.ProcessId}.ndjson");

        await using (var result = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithFileSink(path)
            .WithMinimumLevel("info")
            .BuildAndCommit())
        {
            result.Logger.Information(LogCategory.App,
                "User {UserId} purchased {Sku} for {Price}", 42, "alpha", 9.99);
        }

        Console.WriteLine("  the same event in the JSON file sink:");
        foreach (var line in File.ReadLines(path)) Console.WriteLine("  " + line);
        File.Delete(path);
    }
}
