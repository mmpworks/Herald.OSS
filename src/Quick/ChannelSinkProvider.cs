#nullable enable

using System;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Levels;
using MMP.Herald.Output.Aliases;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Output.Rich;
using MMP.Herald.Output.Writers;
using MMP.Herald.Routing;

namespace MMP.Herald.Quick;

/// <summary>
/// Sink provider for channel-routed output.
/// Creates a sink that uses a custom transformer and writer for a named channel.
/// The channel routing is handled by predicates at the route level, not here.
/// </summary>
public sealed class ChannelSinkProvider : ILogSinkProvider
{
    private readonly string _channelName;
    private readonly ILogOutputTransformer? _customTransformer;
    private readonly IRenderedLogOutputWriter _writer;

    public ChannelSinkProvider(
        string channelName,
        IRenderedLogOutputWriter writer,
        ILogOutputTransformer? customTransformer = null)
    {
        _channelName = channelName;
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _customTransformer = customTransformer;
    }

    /// <summary>
    /// Sink kind is "channel_{name}" so each channel gets a unique kind.
    /// </summary>
    public string SinkKind => $"channel_{_channelName}";

    public ILogger CreateSink(
        LoggingRuntimeSinkDefinition definition,
        ILogLevelRegistry levelRegistry,
        ILogOutputTransformerRegistry transformerRegistry)
    {
        var alias = new LogOutputAlias($"channel_{_channelName}");

        if (_customTransformer is not null)
        {
            // Use the custom transformer directly, bypassing the registry.
            return new DirectTransformerLogger(_customTransformer, levelRegistry, alias, _writer);
        }

        // Fall back to the alias-based transformer from the registry.
        return new RenderedOutputLogger(alias, transformerRegistry, levelRegistry, _writer);
    }
}

/// <summary>
/// Logger that uses a directly-injected transformer instead of looking one up in the registry.
/// Used for channel sinks with custom transformers that aren't registered as aliases.
/// </summary>
internal sealed class DirectTransformerLogger : ILogger
{
    private readonly ILogOutputTransformer _transformer;
    private readonly ILogLevelRegistry _levelRegistry;
    private readonly LogOutputAlias _alias;
    private readonly IRenderedLogOutputWriter _writer;

    public DirectTransformerLogger(
        ILogOutputTransformer transformer,
        ILogLevelRegistry levelRegistry,
        LogOutputAlias alias,
        IRenderedLogOutputWriter writer)
    {
        _transformer = transformer;
        _levelRegistry = levelRegistry;
        _alias = alias;
        _writer = writer;
    }

    public void Log(Events.LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var context = new LogRenderContext(logEvent, _alias, _levelRegistry);
        var output = _transformer.Transform(context);
        _writer.Write(output);
    }
}
