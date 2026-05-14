#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using MMP.Herald.Output.Rich;
using MMP.Herald.Pooling;

namespace MMP.Herald.Output.Rendering;
/// <summary>
/// Canonical plain text presentation renderer.
/// </summary>
public sealed class StandardLogOutputTransformer : ILogOutputTransformer
{
    public RenderedLogOutput Transform(LogRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var registeredLevel = context.LevelRegistry.GetRegisteredLevel(context.Event.Level);
        var builder = StringBuilderPool.Rent();

        builder.Append('[');
        builder.Append(context.Event.TimeUtc.ToString("O"));
        builder.Append("] [");
        builder.Append(registeredLevel.Level.DisplayName);
        builder.Append(':');
        builder.Append(registeredLevel.Rank);
        builder.Append("] ");
        builder.Append(context.Event.Category.Value);
        builder.Append(" - ");
        builder.Append(context.Event.Message);

        var sortedPairs = CollectionPool.RentContextPairs();
        foreach (var pair in context.Event.Context)
        {
            sortedPairs.Add(pair);
        }

        sortedPairs.Sort(static (a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal));

        foreach (var pair in sortedPairs)
        {
            builder.Append(' ');
            builder.Append(pair.Key);
            builder.Append('=');
            builder.Append(pair.Value?.ToString() ?? "null");
        }

        CollectionPool.ReturnContextPairs(sortedPairs);

        return RenderedLogOutput.FromPlainText(StringBuilderPool.ReturnAndGetString(builder));
    }
}