#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using L1 = MMP.Herald.Serilog.Events;

namespace Serilog.Events;

// Layer-2 mirror of Serilog.Events.LogEvent.
// Layer-1 twin: MMP.Herald.Serilog.Events.LogEvent
//
// Construction note: L1.LogEvent constructors are all internal.
// Layer-2 receives a fully-constructed L1 instance via the internal constructor
// below; Layer-1 adapter code (SerilogLoggerAdapter, SerilogEnricherAdapter)
// calls LogEventL2Bridge.Wrap(_inner) to hand the instance across the seam.
public sealed class LogEvent
{
    private readonly L1.LogEvent _inner;

    // The only construction path — receives a ready-made L1 instance.
    internal LogEvent(L1.LogEvent inner) => _inner = inner;

    public DateTimeOffset Timestamp => _inner.Timestamp;

    // Ordinal cast is safe: both enums share identical numeric values.
    public LogEventLevel Level => (LogEventLevel)(int)_inner.Level;

    public string MessageTemplate => _inner.MessageTemplate;

    public string RenderMessage() => _inner.RenderMessage();

    public Exception? Exception => _inner.Exception;

    // Projects the L1 property dictionary to L2 value types via ValueMap.
    public IReadOnlyDictionary<string, LogEventPropertyValue> Properties
        => _inner.Properties.ToDictionary(
            static kv => kv.Key,
            static kv => ValueMap.ToL2(kv.Value),
            StringComparer.Ordinal);

    // Enricher mutators — forward through L1 property conversion.
    public void AddOrUpdateProperty(LogEventProperty property)
        => _inner.AddOrUpdateProperty(property.ToL1());

    public void AddPropertyIfAbsent(LogEventProperty property)
        => _inner.AddPropertyIfAbsent(property.ToL1());

    public void RemovePropertyIfPresent(string propertyName)
        => _inner.RemovePropertyIfPresent(propertyName);
}
