#nullable enable

using System.Collections.Generic;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Output.Rich;
using MMP.Herald.Signals;

namespace MMP.Herald.Quick;

/// <summary>
/// Manages audit sink definitions for a QuickLogBuilder.
/// Audit sinks bypass minimum level filtering.
/// </summary>
public sealed class AuditSinkSet
{
    private readonly QuickLogBuilder _builder;
    internal readonly List<AuditSinkDefinition> Items = new();

    internal AuditSinkSet(QuickLogBuilder builder) => _builder = builder;

    // -- Add --

    /// <summary>Add an audit sink.</summary>
    public QuickLogBuilder Add(
        IRenderedLogOutputWriter writer,
        ILogOutputTransformer? transformer = null,
        ILogSignalHandler? signalHandler = null) {
        Items.Add(new AuditSinkDefinition(writer, transformer, signalHandler));
        return _builder;
    }

    // -- Remove --

    /// <summary>Remove all audit sinks.</summary>
    public QuickLogBuilder Clear() {
        Items.Clear();
        return _builder;
    }

    // -- Query --

    /// <summary>Current audit sink count.</summary>
    public int Count => Items.Count;
}

/// <summary>Audit sink definition.</summary>
internal sealed record AuditSinkDefinition(
    IRenderedLogOutputWriter Writer,
    ILogOutputTransformer? Transformer,
    ILogSignalHandler? SignalHandler);
