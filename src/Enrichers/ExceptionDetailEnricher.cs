#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using MMP.Herald.Events;

namespace MMP.Herald.Enrichers;

/// <summary>
/// Enriches log events that carry an exception in context with structured
/// exception properties. Walks the full InnerException chain and
/// AggregateException children, producing properties that serialize
/// cleanly to JSON and are searchable in log aggregators.
///
/// Added properties:
///   exception.type       - fully qualified type name
///   exception.message    - exception message
///   exception.stackTrace - stack trace (if available)
///   exception.source     - exception source (if available)
///   exception.depth      - nesting depth (0 = root, 1+ = inner)
///   exception.chain      - semicolon-delimited summary of the full chain
///
/// Register via builder:
///   .WithEnrichers(new ExceptionDetailEnricher())
/// </summary>
public sealed class ExceptionDetailEnricher : ILogEnricher
{
    private const int MaxDepth = 10;

    public void Enrich(LogEventEnrichmentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Context.TryGetValue(Services.LogContextKeys.Exception, out var exObj)
            || exObj is not Exception exception)
        {
            return;
        }

        context.AddProperty("exception.type", exception.GetType().FullName ?? exception.GetType().Name);
        context.AddProperty("exception.message", exception.Message);

        if (exception.StackTrace is not null)
        {
            context.AddProperty("exception.stackTrace", exception.StackTrace);
        }

        if (exception.Source is not null)
        {
            context.AddProperty("exception.source", exception.Source);
        }

        // Build the chain summary and count depth
        var chainSummary = BuildChainSummary(exception);
        if (chainSummary.Depth > 0)
        {
            context.AddProperty("exception.depth", chainSummary.Depth);
        }
        context.AddProperty("exception.chain", chainSummary.Summary);
    }

    private static (int Depth, string Summary) BuildChainSummary(Exception root)
    {
        var sb = new StringBuilder();
        var current = root;
        var depth = 0;

        while (current is not null && depth <= MaxDepth)
        {
            if (depth > 0) sb.Append(" -> ");
            sb.Append(current.GetType().Name);
            sb.Append(": ");
            sb.Append(Truncate(current.Message, 100));

            if (current is AggregateException agg && agg.InnerExceptions.Count > 1)
            {
                sb.Append($" ({agg.InnerExceptions.Count} inner)");
            }

            current = current.InnerException;
            depth++;
        }

        if (current is not null)
        {
            sb.Append(" -> ...(truncated)");
        }

        return (depth - 1, sb.ToString());
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "...");
}
