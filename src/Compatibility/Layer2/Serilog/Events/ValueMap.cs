#nullable enable

using L1 = MMP.Herald.Serilog.Events;

namespace Serilog.Events;

// Internal type-identity forwarder: L1 value node → L2 value node.
// This is a type-dispatch switch, not a computation — zero logic per CRIT-FM-L1.
// Each arm is a straight identity forward that creates the L2 wrapper.
internal static class ValueMap
{
    internal static LogEventPropertyValue ToL2(L1.LogEventPropertyValue v) => v switch
    {
        L1.ScalarValue     sv => new ScalarValue(sv),
        L1.StructureValue  st => new StructureValue(st),
        L1.SequenceValue   sq => new SequenceValue(sq),
        L1.DictionaryValue dv => new DictionaryValue(dv),
        _                    => new ScalarValue(v),   // unknown L1 subtype: surface as scalar
    };
}
