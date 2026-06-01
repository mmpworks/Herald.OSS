#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
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
///   exception.stacktrace - stack trace (if available; lowercase per OTel semconv)
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

    // The custom-payload flatten path reflects over the exception type. This
    // enricher only runs when a consumer opts in via WithExceptionDetails()
    // (or explicit WithEnrichers), so OSS Core stays AOT-clean by default.
    // Pattern matches SerilogEnricherAdapter.Enrich:48.
    [SuppressMessage("Trimming", "IL2026",
        Justification = "Reflection-based custom-data flattening is gated behind opt-in enricher registration.")]
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
            // Lowercase per OpenTelemetry exception semantic conventions
            // (`exception.stacktrace`). Matches the wire shape the OTLP
            // encoder emits in OtelLogRecord.FromLogEvent.
            context.AddProperty("exception.stacktrace", exception.StackTrace);
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

        // W3b — flatten the exception's custom payload (public properties +
        // Exception.Data entries) into FLAT dotted-key SCALAR properties so the
        // dropped fields (e.g. OrderException.OrderId = 4071) survive to the sink
        // as first-class queryable keys (exception.data.OrderId). Reflection is
        // trim-unsafe; this enricher is opt-in via WithExceptionDetails(), so OSS
        // Core stays AOT-clean until a consumer registers it.
        FlattenCustomData(context, exception, "exception.", "data", depth: 0);
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

    // ── W3b: custom-payload flattening ────────────────────────────────────────

    /// <summary>
    /// Framework-owned members already surfaced by the flat <c>exception.*</c>
    /// properties (or that carry no custom payload). Reflecting these would
    /// duplicate existing keys or walk infrastructure plumbing, so they are
    /// skipped. What remains is the exception's custom payload.
    /// </summary>
    private static readonly HashSet<string> FrameworkPropertyNames = new(StringComparer.Ordinal)
    {
        nameof(Exception.Message),
        nameof(Exception.StackTrace),
        nameof(Exception.Source),
        nameof(Exception.InnerException),
        nameof(Exception.Data),
        nameof(Exception.HResult),
        nameof(Exception.TargetSite),
        nameof(Exception.HelpLink),
        // AggregateException.InnerExceptions — the children are walked structurally
        // via the indexed data[i] branch, so the collection itself is not a leaf.
        "InnerExceptions",
    };

    /// <summary>
    /// Recursively flattens an exception's custom payload into FLAT dotted-key
    /// SCALAR properties. Three branches, all capped by the shared
    /// <see cref="MaxDepth"/> guard:
    ///   1. Scalar leaf  → <c>context.AddProperty(prefix + dataSegment + "." + key, value)</c>.
    ///   2. AggregateException children → indexed segment <c>data[i]</c>, recurse.
    ///   3. InnerException → append <c>inner.</c> to the prefix, recurse.
    ///
    /// <para>
    /// <paramref name="prefix"/> is the node prefix (<c>exception.</c>,
    /// <c>exception.inner.</c>, …). <paramref name="dataSegment"/> is the label for
    /// THIS node's own leaves: <c>data</c> for a normal node, <c>data[i]</c> for an
    /// AggregateException child. A leaf key is <c>prefix + dataSegment + "." + name</c>,
    /// giving <c>exception.data.OrderId</c>, <c>exception.inner.data.RetryCount</c>, and
    /// <c>exception.data[0].X</c> respectively.
    /// </para>
    ///
    /// <para>
    /// An AggregateException nested under an inner chain keys its children
    /// <c>exception.inner.data[i].Y</c> (the index folds into that node's data
    /// segment). This is still a flat, queryable, non-colliding key.
    /// </para>
    ///
    /// Reflection is the trim-unsafe part; this method is only reachable when a
    /// consumer opts in via <c>WithExceptionDetails()</c>, keeping OSS Core AOT-clean.
    /// </summary>
    [RequiresUnreferencedCode(
        "Reflects an exception's public properties to flatten custom payload. " +
        "Opt-in via WithExceptionDetails(); OSS Core stays AOT-clean without it.")]
    [SuppressMessage("Trimming", "IL2026",
        Justification = "Custom-data flattening uses reflection by design; gated behind opt-in registration. " +
                        "Pattern matches SerilogEnricherAdapter.Enrich.")]
    [SuppressMessage("Trimming", "IL2075",
        Justification = "GetProperties on the runtime exception type is intentional reflection over user payload; opt-in only.")]
    private static void FlattenCustomData(
        LogEventEnrichmentContext context,
        Exception exception,
        string prefix,
        string dataSegment,
        int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        // This node's leaf prefix, e.g. "exception.data." or "exception.data[0].".
        var leafPrefix = prefix + dataSegment + ".";

        // Branch 1: custom public properties (skip the framework set) → scalar leaves.
        foreach (var property in exception.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (FrameworkPropertyNames.Contains(property.Name)
                || property.GetIndexParameters().Length > 0
                || !property.CanRead)
            {
                continue;
            }

            var value = ReadGetter(exception, property);
            EmitLeaf(context, leafPrefix + property.Name, value);
        }

        // Branch 1b: Exception.Data dictionary entries → scalar leaves.
        EmitDataDictionary(context, exception, prefix, leafPrefix);

        // Branch 2: AggregateException children → indexed recurse (data[i]).
        if (exception is AggregateException aggregate)
        {
            var children = aggregate.InnerExceptions;
            for (var index = 0; index < children.Count; index += 1)
            {
                // Index folds into THIS child's own data segment: data[i].X — not a
                // nested data[i].data.X — so recurse with the indexed segment and the
                // SAME node prefix.
                FlattenCustomData(
                    context,
                    children[index],
                    prefix,
                    $"data[{index.ToString(CultureInfo.InvariantCulture)}]",
                    depth + 1);
            }

            // AggregateException.InnerException duplicates InnerExceptions[0];
            // skip the linear branch so the first child isn't emitted twice.
            return;
        }

        // Branch 3: linear InnerException chain → prefixed recurse (inner.data.X).
        if (exception.InnerException is { } inner)
        {
            FlattenCustomData(context, inner, prefix + "inner.", "data", depth + 1);
        }
    }

    /// <summary>
    /// Emits the entries of <see cref="Exception.Data"/> as scalar leaves under
    /// this node's data segment. Non-string keys are rendered with the invariant
    /// culture so the key name is stable across locales.
    /// </summary>
    private static void EmitDataDictionary(
        LogEventEnrichmentContext context,
        Exception exception,
        string prefix,
        string leafPrefix)
    {
        IDictionary data;
        try
        {
            data = exception.Data;
        }
        catch (Exception ex)
        {
            // A hostile override of Data should never crash the log call.
            context.AddProperty(prefix + "data", $"<threw: {ex.GetType().Name}>");
            return;
        }

        if (data.Count == 0)
        {
            return;
        }

        foreach (DictionaryEntry entry in data)
        {
            var key = entry.Key as string
                ?? Convert.ToString(entry.Key, CultureInfo.InvariantCulture)
                ?? "(null)";
            EmitLeaf(context, leafPrefix + key, entry.Value);
        }
    }

    /// <summary>
    /// Reads a property getter behind a try/catch. A throwing getter yields the
    /// sentinel <c>"&lt;threw: TypeName&gt;"</c> instead of crashing the log call —
    /// the ratified defensive-programming direction: surface the anomaly, never
    /// drop the event.
    /// </summary>
    [RequiresUnreferencedCode("Invokes a reflected property getter. Opt-in via WithExceptionDetails().")]
    private static object? ReadGetter(Exception exception, PropertyInfo property)
    {
        try
        {
            return property.GetValue(exception);
        }
        catch (Exception ex)
        {
            // Unwrap the reflection layer so the sentinel names the real fault.
            var actual = (ex as TargetInvocationException)?.InnerException ?? ex;
            return $"<threw: {actual.GetType().Name}>";
        }
    }

    /// <summary>
    /// Emits one FLAT dotted-key property. Scalar CLR values pass through as the
    /// native type (Default/Scalar capture). Anything non-scalar is reduced to its
    /// <c>ToString()</c> form — we never recurse into arbitrary objects, only the
    /// exception chain is walked.
    /// </summary>
    private static void EmitLeaf(LogEventEnrichmentContext context, string name, object? value)
    {
        if (value is null)
        {
            context.AddProperty(name, null);
            return;
        }

        context.AddProperty(name, IsScalar(value) ? value : value.ToString());
    }

    /// <summary>
    /// Scalars render as native JSON leaves; everything else is stringified.
    /// Mirrors the value set the formatter treats as a single rendered token.
    /// </summary>
    private static bool IsScalar(object value) => value switch
    {
        string or bool or char => true,
        sbyte or byte or short or ushort or int or uint or long or ulong => true,
        float or double or decimal => true,
        DateTime or DateTimeOffset or TimeSpan or Guid => true,
        Enum => true,
        _ => false,
    };
}
