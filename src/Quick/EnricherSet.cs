#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Enrichers;

namespace MMP.Herald.Quick;

/// <summary>
/// Manages the enricher list for a QuickLogBuilder.
/// <para>
/// Starts empty — no per-event metadata is computed unless the caller opts
/// in. That keeps the accepted-call path ~290 ns / ~616 B cheaper than the
/// pre-flip default on a 4-property event. The old auto-installed enrichers
/// (MachineName, ProcessId, ThreadId) remain available: call
/// <c>.WithSystemTagsEnrichers()</c> on the builder — or register any of
/// the concrete types individually — when you want them.
/// </para>
/// <para>
/// Defaults were flipped during the accepted-path optimization pass
/// (2026-04-20). Callers who relied on the implicit system tags can
/// re-enable them with one call; callers who didn't now get the fast
/// path by default.
/// </para>
/// </summary>
public sealed class EnricherSet
{
    private readonly QuickLogBuilder _builder;
    internal readonly List<NamedRegistration<ILogEnricher>> Items = new();
    // Defaults are OPT-IN as of the accepted-path pass. Callers flip this
    // back on via WithSystemTagsEnrichers on the builder (or Reset() here).
    private bool _includeDefaults = false;
    private bool _defaultsMaterialized;

    internal EnricherSet(QuickLogBuilder builder) => _builder = builder;

    /// <summary>
    /// Materializes default enrichers into Items. Called lazily at Resolve() time
    /// so that Clear() before Build() avoids the expense entirely.
    /// </summary>
    private void MaterializeDefaults()
    {
        if (!_includeDefaults || _defaultsMaterialized) return;
        _defaultsMaterialized = true;
        // Inserted at position 0 so they run before any user-added enrichers.
        Items.InsertRange(0, new NamedRegistration<ILogEnricher>[]
        {
            new("MachineName", new MachineNameLogEnricher()),
            new("ProcessId", new ProcessIdLogEnricher()),
            new("ThreadId", new ThreadIdLogEnricher())
        });
    }

    // -- Add --

    /// <summary>Add an enricher (named by its type).</summary>
    public QuickLogBuilder Add(ILogEnricher enricher) {
        Items.Add(new NamedRegistration<ILogEnricher>(enricher.GetType().Name, enricher));
        return _builder;
    }

    /// <summary>Add a named enricher.</summary>
    public QuickLogBuilder Add(string name, ILogEnricher enricher) {
        Items.Add(new NamedRegistration<ILogEnricher>(name, enricher));
        return _builder;
    }

    /// <summary>Add multiple enrichers (named by their types).</summary>
    public QuickLogBuilder Add(params ILogEnricher[] enrichers) {
        foreach (var enricher in enrichers)
            Items.Add(new NamedRegistration<ILogEnricher>(enricher.GetType().Name, enricher));
        return _builder;
    }

    // -- Remove --

    /// <summary>Remove an enricher by name.</summary>
    public QuickLogBuilder Remove(string name) {
        MaterializeDefaults();
        Items.RemoveAll(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        return _builder;
    }

    /// <summary>Remove multiple enrichers by name.</summary>
    public QuickLogBuilder Remove(params string[] names) {
        MaterializeDefaults();
        foreach (var name in names)
            Items.RemoveAll(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        return _builder;
    }

    /// <summary>Remove all enrichers (defaults will not be materialized).</summary>
    public QuickLogBuilder Clear() {
        _includeDefaults = false;
        _defaultsMaterialized = false;
        Items.Clear();
        return _builder;
    }

    /// <summary>Remove all and restore defaults (deferred).</summary>
    public QuickLogBuilder Reset() {
        Items.Clear();
        _includeDefaults = true;
        _defaultsMaterialized = false;
        return _builder;
    }

    // -- Query --

    /// <summary>Get a named enricher or null.</summary>
    public T? Get<T>(string name) where T : class, ILogEnricher {
        MaterializeDefaults();
        return NamedItemQuery.Get<T, ILogEnricher>(Items, name);
    }

    /// <summary>Get an enricher by type (first match).</summary>
    public T? Get<T>() where T : class, ILogEnricher {
        MaterializeDefaults();
        return NamedItemQuery.GetByType<T, ILogEnricher>(Items);
    }

    /// <summary>Check if an enricher with the given name exists.</summary>
    public bool Has(string name) {
        MaterializeDefaults();
        return NamedItemQuery.Has(Items, name);
    }

    /// <summary>Current enricher count (includes pending defaults).</summary>
    public int Count {
        get {
            MaterializeDefaults();
            return Items.Count;
        }
    }

    // -- Replace --

    /// <summary>Replace a named enricher. Returns true if found.</summary>
    public bool Replace(string name, ILogEnricher newEnricher) {
        MaterializeDefaults();
        return NamedItemQuery.Replace(Items, name, newEnricher);
    }

    /// <summary>Resolve the final enricher for the pipeline.</summary>
    internal ILogEnricher Resolve()
    {
        MaterializeDefaults();
        return Items.Count switch
        {
            0 => NullLogEnricher.Instance,
            1 => Items[0].Value,
            _ => new CompositeLogEnricher(Items.ConvertAll(static r => r.Value).ToArray())
        };
    }
}
