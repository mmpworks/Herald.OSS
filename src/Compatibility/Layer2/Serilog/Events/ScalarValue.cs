#nullable enable

using L1 = MMP.Herald.Serilog.Events;

namespace Serilog.Events;

// Layer-2 mirror of Serilog.Events.ScalarValue.
// Layer-1 twin: MMP.Herald.Serilog.Events.ScalarValue
// ScalarValue(null) is valid — DO NOT add a null guard (CRIT-FM-L1).
public sealed class ScalarValue : LogEventPropertyValue
{
    private readonly L1.ScalarValue _inner;

    public ScalarValue(object? value) => _inner = new L1.ScalarValue(value);

    // Layer-2 ← Layer-1 lift path: used by value-map helpers in LogEventProperty / LogEvent.
    internal ScalarValue(L1.ScalarValue inner) => _inner = inner;

    public object? Value => _inner.Value;

    internal override L1.LogEventPropertyValue InnerValue => _inner;
}
