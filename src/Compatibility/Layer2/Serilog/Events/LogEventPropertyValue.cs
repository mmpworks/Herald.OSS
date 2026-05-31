#nullable enable

using L1 = MMP.Herald.Serilog.Events;

namespace Serilog.Events;

// Abstract base for the Layer-2 Serilog.Events value-node hierarchy.
// Layer-1 twin: MMP.Herald.Serilog.Events.LogEventPropertyValue
// The L1 abstract base exposes no members (no Render) — mirror matches exactly.
// Each concrete subtype carries an L1 inner instance and projects properties forward.
public abstract class LogEventPropertyValue
{
    // Packages the L1 instance so the type-dispatch helper in LogEventProperty
    // and LogEvent can extract the L1 reference without exposing it publicly.
    internal abstract L1.LogEventPropertyValue InnerValue { get; }
}
