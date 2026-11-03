using MMP.Herald.Configuration;
using MMP.Herald.Events;
using MMP.Herald.Quick;

namespace LoggingPractices.Practices;

/// <summary>
/// Practice 8. Give each log type its own lifetime. Each file sink takes a
/// FileSinkPolicy: rolling interval, minimum level, retention days, and a
/// total size cap. Here the debug trail keeps 3 days and the audit trail keeps 400.
/// </summary>
public static class RetentionPolicies
{
    public static async Task RunAsync()
    {
        var dir = Directory.CreateTempSubdirectory("herald-retention-").FullName;

        var debugTrail = FileSinkPolicy.Create().Daily()
            .WithMinLevel("debug").WithRetentionDays(3).WithTotalSizeCap(200L * 1024 * 1024);
        var auditTrail = FileSinkPolicy.Create().Daily()
            .WithMinLevel("warning").WithRetentionDays(400).WithTotalSizeCap(5L * 1024 * 1024 * 1024);

        await Write(Path.Combine(dir, "debug.log"), debugTrail);
        await Write(Path.Combine(dir, "audit.ndjson"), auditTrail);

        Console.WriteLine($"  debug trail policy: {debugTrail}");
        Console.WriteLine($"  audit trail policy: {auditTrail}");
        foreach (var file in Directory.GetFiles(dir).Order())
            Console.WriteLine($"  {Path.GetFileName(file),-28} {CountLines(file)} lines");
    }

    // The rolling sink may still hold its handle for a moment after dispose,
    // so read with shared access.
    private static int CountLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var count = 0;
        while (reader.ReadLine() is not null) count++;
        return count;
    }

    private static async Task Write(string path, FileSinkPolicy policy)
    {
        await using var result = QuickLogBuilder.Create()
            .WithoutConsoleSink()
            .WithMinimumLevel("debug")
            .WithFileSink(path, policy)
            .BuildAndCommit();

        result.Logger.Debug(LogCategory.App, "Cache miss for {Key}", "user:42");
        result.Logger.Information(LogCategory.App, "User {UserId} signed in", 42);
        result.Logger.Warning(LogCategory.App, "Role changed for {UserId} to {Role}", 42, "admin");
    }
}
