#nullable enable

namespace MMP.Herald.Serilog.Events;

/// <summary>
/// Serilog-shaped log level enum. Ordinal values match real Serilog's so
/// consumers comparing <c>&gt;=</c> get Serilog semantics.
/// </summary>
public enum LogEventLevel
{
    Verbose     = 0,
    Debug       = 1,
    Information = 2,
    Warning     = 3,
    Error       = 4,
    Fatal       = 5,
}
