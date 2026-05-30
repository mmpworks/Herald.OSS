#nullable enable

using MMP.Herald.Quick;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Destructuring;

namespace MMP.Herald.Serilog.Configuration;

public sealed class LoggerConfiguration
{
    internal QuickLogBuilder Builder { get; } = QuickLogBuilder.Create();

    // S5 seam: shared applicator for Serilog IDestructuringPolicy registrations.
    // LoggerDestructuringConfiguration.With() adds policies here;
    // LoggerSinkConfiguration passes this to SerilogSinkAdapter so the adapter
    // can thread it to the mirror LogEvent constructor at dispatch time.
    internal SerilogDestructuringApplicator SerilogPolicyApplicator { get; } = new();

    public MinimumLevelConfiguration MinimumLevel { get; }
    public LoggerSinkConfiguration WriteTo { get; }
    public LoggerEnrichmentConfiguration Enrich { get; }
    public LoggerDestructuringConfiguration Destructure { get; }
    public LoggerConfiguration ReadFrom => this;

    public LoggerConfiguration()
    {
        MinimumLevel = new MinimumLevelConfiguration(this);
        WriteTo = new LoggerSinkConfiguration(this);
        Enrich = new LoggerEnrichmentConfiguration(this);
        Destructure = new LoggerDestructuringConfiguration(this);
    }

    public ILogger CreateLogger()
        => SerilogLoggerAdapter.FromBuild(Builder.Build());
}
