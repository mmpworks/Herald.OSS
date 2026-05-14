#nullable enable

using System;
using MMP.Herald.Expansions;
using MMP.Herald.Output.Rich;

namespace MMP.Herald.Output.Rendering;
/// <summary>
/// Wraps a base transformer and then applies level expansions.
/// </summary>
public sealed class CompositeLogOutputTransformer : ILogOutputTransformer
{
    private readonly ILogOutputTransformer _baseTransformer;
    private readonly ILogLevelOutputExpansionRegistry _expansionRegistry;

    public CompositeLogOutputTransformer(
        ILogOutputTransformer baseTransformer,
        ILogLevelOutputExpansionRegistry expansionRegistry)
    {
        _baseTransformer = baseTransformer;
        _expansionRegistry = expansionRegistry;
    }

    public RenderedLogOutput Transform(LogRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var output = _baseTransformer.Transform(context);
        var expansions = _expansionRegistry.Get(context.Event.Level, context.Alias);

        // Cognitive Complexity note:
        // this loop is intentionally linear and registration-ordered so output
        // rewrites stay predictable and easy to debug.
        foreach (var expansion in expansions)
        {
            output = expansion.Transform(context, output);
        }

        return output;
    }
}