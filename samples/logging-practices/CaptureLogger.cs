using MMP.Herald;
using MMP.Herald.Events;

namespace LoggingPractices;

/// <summary>
/// A Herald <see cref="ILogger"/> that keeps every event it receives and can
/// print each one. The samples attach it with <c>WithBridge(...)</c> so the
/// console shows the fields a sink receives, not only the rendered text.
/// </summary>
public sealed class CaptureLogger(bool print = true) : ILogger
{
    private readonly List<LogEvent> _events = [];
    private readonly Lock _sync = new();

    public IReadOnlyList<LogEvent> Events
    {
        get { lock (_sync) return [.. _events]; }
    }

    public void Log(LogEvent logEvent)
    {
        lock (_sync) _events.Add(logEvent);
        if (print) Console.WriteLine("  " + Describe(logEvent));
    }

    public static string Describe(LogEvent e)
    {
        var props = string.Join(", ", e.Properties.Select(p => $"{p.Name}={p.Value}"));
        var id = e.EventId is null ? "" : $" event {e.EventId.Id}:{e.EventId.Name}";
        return $"[{e.Level.Key}]{id} {e.Message}  {{ {props} }}";
    }
}
