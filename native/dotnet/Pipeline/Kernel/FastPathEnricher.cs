#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MMP.Herald.Templating;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Kernel-aware static enricher. Appends a fixed set of properties to
/// the dispatch-time property span before the
/// <see cref="LogEventBuffer"/> is constructed. Allocates one new
/// array per accepted event (size = input + static-add count); makes
/// no other allocation.
///
/// <para>
/// <b>Why this exists.</b> The legacy <see cref="Enrichers.ILogEnricher"/>
/// path mutates a <see cref="Events.LogEventEnrichmentContext"/> the
/// chain materialises ahead of the enricher call — every accepted
/// event pays the heap-event allocation regardless of whether
/// enrichment runs. The kernel-aware shape lets the dispatch site
/// allocate exactly the array it needs and hand it straight to the
/// kernel buffer. No event factory, no enrichment context, no list
/// resizing.
/// </para>
///
/// <para>
/// <b>Scope.</b> Static (constant-value) enrichment only — the
/// dominant production case (<c>service.name</c>, <c>host.name</c>,
/// <c>deployment.environment</c>, fixed tag set). Dynamic enrichers
/// that compute a value per call (traceId, request-scoped properties)
/// stay on the existing <see cref="Enrichers.ILogEnricher"/> path until
/// a separate experiment validates that shape.
/// </para>
/// </summary>
public sealed class FastPathEnricher
{
    private readonly LogProperty[] _statics;
    private readonly LogPropertyCompact[] _staticsCompact;

    public FastPathEnricher(IReadOnlyList<LogProperty> staticProperties)
    {
        ArgumentNullException.ThrowIfNull(staticProperties);
        _statics = new LogProperty[staticProperties.Count];
        _staticsCompact = new LogPropertyCompact[staticProperties.Count];
        for (var i = 0; i < staticProperties.Count; i++)
        {
            var p = staticProperties[i];
            _statics[i] = p;
            _staticsCompact[i] = new LogPropertyCompact(p.Name, p.ResolvedValue);
        }
    }

    /// <summary>Number of static properties this enricher appends.</summary>
    public int Count => _statics.Length;

    // ── LogProperty path ─────────────────────────────────────────────

    /// <summary>
    /// Apply enrichment to a span of <see cref="LogProperty"/>. Returns
    /// the input span unchanged when no static properties are configured
    /// (degenerate case). Otherwise returns a span over a freshly
    /// allocated array of size <c>input.Length + Count</c> with the
    /// originals first, statics appended.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<LogProperty> Apply(ReadOnlySpan<LogProperty> input)
    {
        if (_statics.Length == 0) return input;
        return AppendStatics(input);
    }

    private LogProperty[] AppendStatics(ReadOnlySpan<LogProperty> input)
    {
        var output = new LogProperty[input.Length + _statics.Length];
        input.CopyTo(output);
        _statics.AsSpan().CopyTo(output.AsSpan(input.Length));
        return output;
    }

    // ── LogPropertyCompact path ──────────────────────────────────────

    /// <summary>
    /// Apply enrichment to a span of <see cref="LogPropertyCompact"/>.
    /// Same shape as the legacy overload; appends pre-built compact
    /// records so the conversion runs once at construction, not per call.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<LogPropertyCompact> Apply(ReadOnlySpan<LogPropertyCompact> input)
    {
        if (_staticsCompact.Length == 0) return input;
        return AppendStaticsCompact(input);
    }

    private LogPropertyCompact[] AppendStaticsCompact(ReadOnlySpan<LogPropertyCompact> input)
    {
        var output = new LogPropertyCompact[input.Length + _staticsCompact.Length];
        input.CopyTo(output);
        _staticsCompact.AsSpan().CopyTo(output.AsSpan(input.Length));
        return output;
    }
}
