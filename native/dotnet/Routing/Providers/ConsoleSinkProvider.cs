#nullable enable

using System;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Levels;
using MMP.Herald.Output.Aliases;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Output.Rich;
using MMP.Herald.Output.Writers;

namespace MMP.Herald.Routing.Providers;

/// <summary>
/// Sink provider for rich console output.
/// </summary>
public sealed class ConsoleSinkProvider : ILogSinkProvider
{
    private readonly IRenderedLogOutputWriter _richConsoleWriter;

    public ConsoleSinkProvider(IRenderedLogOutputWriter richConsoleWriter)
    {
        _richConsoleWriter = richConsoleWriter ?? throw new ArgumentNullException(nameof(richConsoleWriter));
    }

    public string SinkKind => Services.KnownSinkKinds.Console;

    public ILogger CreateSink(
        LoggingRuntimeSinkDefinition definition,
        ILogLevelRegistry levelRegistry,
        ILogOutputTransformerRegistry transformerRegistry)
    {
        var alias = definition.Alias is null
            ? KnownLogOutputAliases.Console
            : new LogOutputAlias(definition.Alias);

        return new RenderedOutputLogger(alias, transformerRegistry, levelRegistry, _richConsoleWriter);
    }
}
