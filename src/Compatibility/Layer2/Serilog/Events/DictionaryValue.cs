#nullable enable

using System.Collections.Generic;
using System.Linq;
using L1 = MMP.Herald.Serilog.Events;

namespace Serilog.Events;

// Layer-2 mirror of Serilog.Events.DictionaryValue.
// Layer-1 twin: MMP.Herald.Serilog.Events.DictionaryValue
public sealed class DictionaryValue : LogEventPropertyValue
{
    private readonly L1.DictionaryValue _inner;

    public DictionaryValue(IEnumerable<KeyValuePair<ScalarValue, LogEventPropertyValue>> elements)
        => _inner = new L1.DictionaryValue(
            elements.Select(static kv =>
                new KeyValuePair<L1.ScalarValue, L1.LogEventPropertyValue>(
                    new L1.ScalarValue(kv.Key.Value),
                    kv.Value.InnerValue)));

    // Layer-2 ← Layer-1 lift path.
    internal DictionaryValue(L1.DictionaryValue inner) => _inner = inner;

    public IReadOnlyList<KeyValuePair<ScalarValue, LogEventPropertyValue>> Elements
        => _inner.Elements
            .Select(static kv => new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                new ScalarValue(kv.Key.Value),
                ValueMap.ToL2(kv.Value)))
            .ToArray();

    internal override L1.LogEventPropertyValue InnerValue => _inner;
}
