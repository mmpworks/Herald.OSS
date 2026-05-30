#nullable enable

using System.Collections.Generic;
using System.Linq;
using L1 = MMP.Herald.Serilog.Events;

namespace Serilog.Events;

// Layer-2 mirror of Serilog.Events.StructureValue.
// Layer-1 twin: MMP.Herald.Serilog.Events.StructureValue
public sealed class StructureValue : LogEventPropertyValue
{
    private readonly L1.StructureValue _inner;

    public StructureValue(IEnumerable<LogEventProperty> properties, string? typeTag = null)
        => _inner = new L1.StructureValue(properties.Select(static p => p.ToL1()), typeTag);

    // Layer-2 ← Layer-1 lift path.
    internal StructureValue(L1.StructureValue inner) => _inner = inner;

    public IReadOnlyList<LogEventProperty> Properties
        => _inner.Properties.Select(static p => LogEventProperty.FromL1(p)).ToArray();

    public string? TypeTag => _inner.TypeTag;

    internal override L1.LogEventPropertyValue InnerValue => _inner;
}
