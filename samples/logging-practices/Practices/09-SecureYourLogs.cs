using System.Security.Cryptography;
using MMP.Herald.Addons.Compliance;
using MMP.Herald.Events;
using MMP.Herald.Quick;

namespace LoggingPractices.Practices;

/// <summary>
/// Practice 9. Make the log tamper-evident. HmacChainLogger chains each event
/// to the one before it with HMAC-SHA256. VerifyChain replays the chain and
/// returns the index of the first altered event, or -1 when the chain is intact.
/// Encryption in transit, encryption at rest, and access control belong to the log store.
/// </summary>
public static class SecureYourLogs
{
    public static async Task RunAsync()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var store = new CaptureLogger(print: false);
        using var chain = new HmacChainLogger(store, key);

        await using (var result = QuickLogBuilder.Create()
            .WithoutConsoleSink()
            .WithBridge(chain)
            .BuildAndCommit())
        {
            result.Logger.Information(LogCategory.App, "User {UserId} signed in", 42);
            result.Logger.Warning(LogCategory.App, "Role changed for {UserId} to {Role}", 42, "admin");
            result.Logger.Information(LogCategory.App, "User {UserId} signed out", 42);
        }

        var events = store.Events.ToList();
        foreach (var e in events)
            Console.WriteLine($"  {e.Message,-36} chain {e.Context[ComplianceContextKeys.HmacChain]?.ToString()?[..16]}...");

        Console.WriteLine($"  verify original: {HmacChainLogger.VerifyChain(events, key)}  (-1 = intact)");

        events[1] = events[1] with { Message = "Role changed for 42 to viewer" };
        Console.WriteLine($"  verify tampered: {HmacChainLogger.VerifyChain(events, key)}  (index of the first altered event)");
    }
}
