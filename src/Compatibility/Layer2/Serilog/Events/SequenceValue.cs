#nullable enable

using System.Collections.Generic;
using System.Linq;
using L1 = MMP.Herald.Serilog.Events;

namespace Serilog.Events;

// Layer-2 mirror of Serilog.Events.SequenceValue.
// Layer-1 twin: MMP.Herald.Serilog.Events.SequenceValue
public sealed class SequenceValue : LogEventPropertyValue
{
    private readonly L1.SequenceValue _inner;

    public SequenceValue(IEnumerable<LogEventPropertyValue> elements)
        => _inner = new L1.SequenceValue(elements.Select(static e => e.InnerValue));

    // Layer-2 ← Layer-1 lift path.
    internal SequenceValue(L1.SequenceValue inner) => _inner = inner;

    public IReadOnlyList<LogEventPropertyValue> Elements
        => _inner.Elements.Select(static v => ValueMap.ToL2(v)).ToArray();

    internal override L1.LogEventPropertyValue InnerValue => _inner;
}
