#nullable enable

using MMP.Herald.Bootstrap;
using MMP.Herald.Configuration;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Pipeline;
using MMP.Herald.Time;

namespace MMP.Herald.Quick;

/// <summary>
/// The result of QuickLogBuilder.Build() - a fully constructed pipeline that is
/// NOT yet live. Call Commit() on the QuickLogResult to swap it in, or inspect
/// it for previewing proposed configurations.
///
/// This separates "what was built" from "what is live":
///   var proposal = builder.Build();           // construct but don't activate
///   var json = proposal.ExportConfig();        // inspect the proposed config
///   result.Commit(proposal);                   // make it live (atomic swap)
///
/// Or use the convenience method:
///   var result = builder.BuildAndCommit();      // Build() + auto-Commit()
/// </summary>
public sealed class PipelineBuildResult
{
    internal PipelineBuildResult(
        StructuredLogger logger,
        LoggingBootstrapResult bootstrapResult,
        IDateTimeProvider timeProvider,
        PipelineAccessor pipelineAccessor,
        string configJson,
        string? pipelineName = null)
    {
        Logger = logger;
        BootstrapResult = bootstrapResult;
        TimeProvider = timeProvider;
        PipelineAccessor = pipelineAccessor;
        ConfigJson = configJson;
        PipelineName = pipelineName;
    }

    /// <summary>The constructed logger (not yet active).</summary>
    internal StructuredLogger Logger { get; }

    /// <summary>The full bootstrap result.</summary>
    internal LoggingBootstrapResult BootstrapResult { get; }

    /// <summary>The time provider used for this build.</summary>
    internal IDateTimeProvider TimeProvider { get; }

    /// <summary>The pipeline accessor with all registered components.</summary>
    internal PipelineAccessor PipelineAccessor { get; }

    /// <summary>The name the builder registered this pipeline under, or null when unnamed.</summary>
    internal string? PipelineName { get; }

    /// <summary>
    /// The serialized JSON configuration that this build represents.
    /// This is what would be written to disk on commit.
    /// </summary>
    public string ConfigJson { get; }

    /// <summary>
    /// Export the proposed configuration as formatted JSON.
    /// This is the exact config that would be saved if committed.
    /// </summary>
    public string ExportConfig() => ConfigJson;
}
