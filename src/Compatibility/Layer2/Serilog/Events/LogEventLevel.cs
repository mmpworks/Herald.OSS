#nullable enable

namespace Serilog.Events;

// Plain enum — no Layer-1 reference needed.
// Ordinal values match real Serilog's exactly so consumer comparisons (>=, ==) work unchanged.
// Layer-1 twin: MMP.Herald.Serilog.Events.LogEventLevel (same values, different namespace).
public enum LogEventLevel
{
    Verbose     = 0,
    Debug       = 1,
    Information = 2,
    Warning     = 3,
    Error       = 4,
    Fatal       = 5,
}
