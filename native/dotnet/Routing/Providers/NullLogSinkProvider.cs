#nullable enable

using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Pipeline;

namespace MMP.Herald.Routing.Providers;

/// <summary>
/// Sink provider that drops every event. Useful for benchmarks that
/// measure pipeline cost in isolation, for temporarily muting a pipeline,
/// and for configurations that exist only to exercise the pipeline
/// without downstream I/O.
/// </summary>
public sealed class NullLogSinkProvider : ILogSinkProvider
{
    public string SinkKind => Services.KnownSinkKinds.Null;

    public ILogger CreateSink(
        LoggingRuntimeSinkDefinition definition,
        ILogLevelRegistry levelRegistry,
        ILogOutputTransformerRegistry transformerRegistry) =>
        NoOpLogger.Instance;
}
