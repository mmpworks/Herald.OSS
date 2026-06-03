#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Events;
using MMP.Herald.Filters;

namespace MMP.Herald.Serilog.Configuration;

/// <summary>
/// Fluent <c>Filter.*</c> step on the compat <see cref="LoggerConfiguration"/>.
/// Mirrors Serilog's <c>LoggerConfiguration.Filter</c>: a migrated
/// <c>.Filter.ByExcluding(...)</c> / <c>.Filter.ByIncludingOnly(...)</c> /
/// <c>.Filter.With(...)</c> call gates the Herald pipeline.
///
/// <para>
/// <b>Composition.</b> The Herald builder exposes a single filter slot
/// (last-write-wins). To honour Serilog's "every registered filter applies"
/// semantics, this accessor keeps its own list and reinstalls a composed
/// predicate on every add: an event is admitted only if EVERY registered
/// filter admits it (logical AND), matching Serilog's filter chain.
/// </para>
///
/// <para>
/// <b>String-DSL forms.</b> The <c>ByExcluding(string)</c> /
/// <c>ByIncludingOnly(string)</c> Serilog-Expressions overloads are not on this
/// type — they live as extensions in the Apache-2.0
/// <c>MMP.Herald.Serilog.Expressions</c> package so the core compat assembly
/// carries no expression-engine dependency. An app that imports
/// <c>Herald.OSS.Serilog.Expressions.Filtering</c> gets the string overloads on
/// this same accessor; the predicate and <see cref="ILogFilter"/> forms below
/// are always available.
/// </para>
/// </summary>
public sealed class LoggerFilterConfiguration
{
    private readonly LoggerConfiguration _root;

    // All filters registered via this accessor, in registration order. The
    // composed predicate ANDs them so every filter applies (Serilog semantics).
    private readonly List<ILogFilter> _filters = new();

    internal LoggerFilterConfiguration(LoggerConfiguration root) => _root = root;

    /// <summary>
    /// Apply an <see cref="ILogFilter"/> directly. The pipeline admits an event
    /// only when this (and every other registered) filter admits it. This is the
    /// integration point the string-DSL extensions call after compiling an
    /// expression to an <see cref="ILogFilter"/>.
    /// </summary>
    /// <param name="filter">The filter to add. Must not be null.</param>
    public LoggerConfiguration With(ILogFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _filters.Add(filter);
        Reinstall();
        return _root;
    }

    /// <summary>
    /// Admit all events EXCEPT those for which <paramref name="predicate"/>
    /// returns <c>true</c>. Predicate form of Serilog's
    /// <c>Filter.ByExcluding</c> — always available (no expression engine).
    /// </summary>
    public LoggerConfiguration ByExcluding(Func<LogEvent, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return With(new FuncLogFilter(e => !predicate(e)));
    }

    /// <summary>
    /// Admit ONLY events for which <paramref name="predicate"/> returns
    /// <c>true</c>. Predicate form of Serilog's <c>Filter.ByIncludingOnly</c>.
    /// </summary>
    public LoggerConfiguration ByIncludingOnly(Func<LogEvent, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return With(new FuncLogFilter(predicate));
    }

    // Compose all registered filters into one AND predicate and install it on
    // the builder's single filter slot. Reinstalled on every add so the slot
    // always reflects the full chain, never just the last filter.
    private void Reinstall()
    {
        // Snapshot to an array so the installed predicate does not observe later
        // additions through the mutable list (the predicate is captured once here,
        // but a fresh snapshot keeps the closure's view immutable and explicit).
        var snapshot = _filters.ToArray();
        _root.Builder.WithCustomFilter(e =>
        {
            foreach (var filter in snapshot)
            {
                if (!filter.Allow(e)) return false;
            }
            return true;
        });
    }
}
