#nullable enable

using MMP.Herald.Quick;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Destructuring;
using MMP.Herald.Serilog.Sinks;

namespace MMP.Herald.Serilog.Configuration;

public sealed class LoggerConfiguration
{
    internal QuickLogBuilder Builder { get; } = QuickLogBuilder.Create();

    // S5 seam: shared applicator for Serilog IDestructuringPolicy registrations.
    // LoggerDestructuringConfiguration.With() adds policies here;
    // LoggerSinkConfiguration passes this to SerilogSinkAdapter so the adapter
    // can thread it to the mirror LogEvent constructor at dispatch time.
    internal SerilogDestructuringApplicator SerilogPolicyApplicator { get; } = new();

    // Shared adapter: both WriteTo.Sink() and AuditTo.Sink() register their sinks
    // here so that mixed configurations (WriteTo + AuditTo) route through a single
    // null-kind slot without collision.  Lazily registered the first time either
    // channel calls Sink() — see LoggerSinkConfiguration.EnsureAdapterRegistered().
    internal SerilogSinkAdapter? SharedSinkAdapter { get; set; }

    public MinimumLevelConfiguration MinimumLevel { get; }
    public LoggerSinkConfiguration WriteTo { get; }

    /// <summary>
    /// Audit sink configuration — exceptions from sinks registered here propagate
    /// to the caller instead of being swallowed.  Use for compliance-critical paths
    /// where silent delivery failure is unacceptable.
    ///
    /// <para>
    /// Mirrors <c>Serilog.LoggerConfiguration.AuditTo</c>.  Every method on this
    /// object behaves identically to its <see cref="WriteTo"/> counterpart except
    /// that sink failures surface as <c>AuditLogFailureException</c> rather than
    /// being silently dropped.
    /// </para>
    /// </summary>
    public LoggerSinkConfiguration AuditTo { get; }

    public LoggerEnrichmentConfiguration Enrich { get; }
    public LoggerDestructuringConfiguration Destructure { get; }
    public LoggerConfiguration ReadFrom => this;

    public LoggerConfiguration()
    {
        MinimumLevel = new MinimumLevelConfiguration(this);
        WriteTo = new LoggerSinkConfiguration(this, defaultAuditMode: false);
        AuditTo = new LoggerSinkConfiguration(this, defaultAuditMode: true);
        Enrich = new LoggerEnrichmentConfiguration(this);
        Destructure = new LoggerDestructuringConfiguration(this);
    }

    public ILogger CreateLogger()
        => SerilogLoggerAdapter.FromBuild(Builder.Build());
}
