using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Quick;
using MMP.Herald.Templating;

namespace LoggingPractices.Practices;

/// <summary>
/// Practice 1. Decide what to log before you write a log statement.
/// The decision lives in code: one catalog of event ids and categories,
/// written before any call site uses them.
/// </summary>
public static class ClearObjectives
{
    // The catalog. Each entry answers "what question will this event answer?"
    private static readonly LogCategory Orders = new("Orders");
    private static readonly LogEventId OrderPlaced = new(1001, "OrderPlaced");      // revenue and volume
    private static readonly LogEventId PaymentDeclined = new(1002, "PaymentDeclined"); // support follow-up

    public static async Task RunAsync()
    {
        await using var result = QuickLogBuilder.Create()
            .WithoutConsoleSink()
            .WithBridge(new CaptureLogger())
            .WithMinimumLevel("info")
            .BuildAndCommit();

        result.Logger.Log(KnownLogLevels.Information, Orders,
            "Order {OrderId} placed for {Total}",
            [new LogProperty("OrderId", "A-17"), new LogProperty("Total", 42.50m)],
            eventId: OrderPlaced);

        result.Logger.Log(KnownLogLevels.Warning, Orders,
            "Payment declined for order {OrderId}",
            [new LogProperty("OrderId", "A-18")],
            eventId: PaymentDeclined);
    }
}
