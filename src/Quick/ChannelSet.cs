#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Output.Rich;
using MMP.Herald.Signals;

namespace MMP.Herald.Quick;

/// <summary>
/// Manages channel sink definitions for a QuickLogBuilder.
/// Channels route events by name to custom transformers and writers.
/// </summary>
public sealed class ChannelSet
{
    private readonly QuickLogBuilder _builder;
    internal readonly List<ChannelDefinition> Items = new();

    internal ChannelSet(QuickLogBuilder builder) => _builder = builder;

    // -- Add --

    /// <summary>Add a channel with a custom transformer and writer.</summary>
    public QuickLogBuilder Add(string name, ILogOutputTransformer transformer, IRenderedLogOutputWriter writer) {
        Items.Add(new ChannelDefinition(name, writer, transformer, null));
        return _builder;
    }

    /// <summary>Add a channel with a custom transformer, writer, and signal handler.</summary>
    public QuickLogBuilder Add(string name, ILogOutputTransformer transformer,
        IRenderedLogOutputWriter writer, ILogSignalHandler signalHandler) {
        Items.Add(new ChannelDefinition(name, writer, transformer, signalHandler));
        return _builder;
    }

    /// <summary>Add a channel using the default console transformer and a custom writer.</summary>
    public QuickLogBuilder Add(string name, IRenderedLogOutputWriter writer) {
        Items.Add(new ChannelDefinition(name, writer, null, null));
        return _builder;
    }

    /// <summary>Add a channel with a custom writer and signal handler.</summary>
    public QuickLogBuilder Add(string name, IRenderedLogOutputWriter writer, ILogSignalHandler signalHandler) {
        Items.Add(new ChannelDefinition(name, writer, null, signalHandler));
        return _builder;
    }

    // -- Remove --

    /// <summary>Remove a channel by name.</summary>
    public QuickLogBuilder Remove(string name) {
        NamedItemQuery.Remove(Items, static c => c.Name, name);
        return _builder;
    }

    /// <summary>Remove all channels.</summary>
    public QuickLogBuilder Clear() {
        Items.Clear();
        return _builder;
    }

    // -- Query --

    /// <summary>Get a channel definition by name, or null.</summary>
    internal ChannelDefinition? Get(string name) =>
        NamedItemQuery.Find(Items, static c => c.Name, name);

    /// <summary>Check if a channel with the given name exists.</summary>
    public bool Has(string name) =>
        NamedItemQuery.Has(Items, static c => c.Name, name);

    /// <summary>Current channel count.</summary>
    public int Count => Items.Count;
}

/// <summary>Channel sink definition.</summary>
internal sealed record ChannelDefinition(
    string Name,
    IRenderedLogOutputWriter Writer,
    ILogOutputTransformer? Transformer,
    ILogSignalHandler? SignalHandler);
