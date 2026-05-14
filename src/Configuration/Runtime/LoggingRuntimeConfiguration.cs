// Lines 1-18
#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Configuration.Runtime;
/// <summary>
/// Runtime logging configuration after JSON transport types have been normalized.
///
/// <para><b>Loopback knobs.</b>
/// <see cref="TestLoopbackUrl"/> and <see cref="TestLoopbackLogDir"/> are
/// the pipeline-level destinations every sink in this pipeline can tee
/// to (live) or redirect to (test). A sink's own
/// <c>RunState</c> + <c>LiveLoopback*</c> flags decide whether each
/// channel actually receives the event. <see cref="LoopbackEntriesPerFile"/>
/// is the rotation cap on the loopback log files —
/// <c>{TestLoopbackLogDir}/{TestOutFileSuffix}-{SinkName}.log</c> rolls
/// to a new file each time it crosses this count.</para>
///
/// <para><b>File format.</b> <see cref="LoopbackUseNdjson"/> chooses the
/// shape of the teed log files. <c>true</c> (default) writes NDJSON —
/// one structured event per line, machine-parseable, and the same
/// shape the loopback URL receives. <c>false</c> writes plain text
/// rendered through the pipeline's output template — readable in a
/// terminal but not trivially parseable. The URL leg is always
/// NDJSON regardless.</para>
/// </summary>
public sealed record LoggingRuntimeConfiguration(
    LoggingRuntimeLevelsConfiguration Levels,
    LoggingRuntimePresentationConfiguration Presentation,
    LogPipelinePolicy PipelinePolicy,
    IReadOnlyList<LoggingRuntimeSinkDefinition> Sinks,
    IReadOnlyList<LoggingRuntimeRouteDefinition> Routes,
    bool DumpRegisteredLevelsToConsole,
    bool IncludeCallerInfo = false,
    bool IncludeActivityContext = false,
    string? TestLoopbackUrl = null,
    string? TestLoopbackLogDir = null,
    int LoopbackEntriesPerFile = 1000,
    bool LoopbackUseNdjson = true,
    string? PipelineName = null);