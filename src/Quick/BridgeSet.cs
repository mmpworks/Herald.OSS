#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;

namespace MMP.Herald.Quick;

/// <summary>
/// Manages pipeline bridge definitions for a QuickLogBuilder.
/// Bridges forward events from this pipeline into other pipelines.
/// </summary>
public sealed class BridgeSet
{
    private readonly QuickLogBuilder _builder;
    internal readonly List<PipelineBridge> Items = new();

    internal BridgeSet(QuickLogBuilder builder) => _builder = builder;

    // -- Add --

    /// <summary>Add a bridge to another pipeline.</summary>
    public QuickLogBuilder Add(ILogger target) {
        Items.Add(new PipelineBridge(target));
        return _builder;
    }

    /// <summary>Add a filtered bridge to another pipeline.</summary>
    public QuickLogBuilder Add(ILogger target, Func<LogEvent, bool> filter) {
        Items.Add(new PipelineBridge(target, filter));
        return _builder;
    }

    // -- Remove --

    /// <summary>Remove all bridges.</summary>
    public QuickLogBuilder Clear() {
        Items.Clear();
        return _builder;
    }

    // -- Query --

    /// <summary>Current bridge count.</summary>
    public int Count => Items.Count;
}
