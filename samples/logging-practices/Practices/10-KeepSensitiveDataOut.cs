using System.Security.Cryptography;
using MMP.Herald.Events;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Pipeline.Processors;
using MMP.Herald.Quick;
using MMP.Herald.Templating;

namespace LoggingPractices.Practices;

/// <summary>
/// Practice 10. Keep sensitive data out of the log.
/// CompiledRedactionProcessor (builder: WithCompiledRedaction) masks, hashes, or
/// replaces named properties before any sink sees them. FastPathRedactor
/// (builder: WithFastRedaction) applies the same rule shape on the kernel fast path.
/// LogPropertyVisibility.PiiSensitive tags a property as personal data.
/// </summary>
public static class KeepSensitiveDataOut
{
    public static async Task RunAsync()
    {
        var hashKey = RandomNumberGenerator.GetBytes(32);

        Console.WriteLine("  CompiledRedactionProcessor (what a sink receives):");
        var capture = new CaptureLogger();
        await using (var result = QuickLogBuilder.Create()
            .WithoutConsoleSink()
            .WithCompiledRedaction(
                new CompiledRedactionRule("email", RedactionMode.Mask, VisibleChars: 3),
                new CompiledRedactionRule("cardNumber", RedactionMode.Remove),   // replaces the value with [REDACTED]
                new CompiledRedactionRule("taxId", RedactionMode.KeyedHash) { HashKey = hashKey })
            .WithBridge(capture)
            .BuildAndCommit())
        {
            result.Logger.Information(LogCategory.App,
                "Receipt sent to {email} for card {cardNumber}",
                new LogProperty("email", "kaelen@example.com"),
                new LogProperty("cardNumber", "4111111111111111"));

            result.Logger.Information(LogCategory.App,
                "Identity check passed",
                new LogProperty("taxId", "123-45-6789", Visibility: LogPropertyVisibility.PiiSensitive));
        }

        Console.WriteLine("  FastPathRedactor (kernel fast path, console sink):");
        await using (var result = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithFastRedaction(new CompiledRedactionRule("email", RedactionMode.Mask, VisibleChars: 3))
            .BuildAndCommit())
        {
            result.Logger.Information(LogCategory.App, "Password reset sent to {email}",
                new LogProperty("email", "kaelen@example.com"));
        }
    }
}
