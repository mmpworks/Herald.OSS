#nullable enable

using L1 = MMP.Herald.Serilog.Events;

namespace Serilog.Events;

// Layer-2 mirror of Serilog.Events.LogEventProperty.
// Layer-1 twin: MMP.Herald.Serilog.Events.LogEventProperty
// DO NOT add null guards in Layer-2 constructors (CRIT-FM-L1).
// L1's constructor already guards name and value — that guard lives in L1.
public sealed class LogEventProperty
{
    private readonly L1.LogEventProperty _inner;

    public LogEventProperty(string name, LogEventPropertyValue value)
        => _inner = new L1.LogEventProperty(name, value.InnerValue);

    // Layer-2 ← Layer-1 lift path used by StructureValue.Properties.
    private LogEventProperty(L1.LogEventProperty inner) => _inner = inner;

    internal static LogEventProperty FromL1(L1.LogEventProperty p) => new(p);

    // L2 → L1 projection used by StructureValue constructor and mutator forwarders.
    internal L1.LogEventProperty ToL1() => _inner;

    public string Name => _inner.Name;

    public LogEventPropertyValue Value => ValueMap.ToL2(_inner.Value);
}
