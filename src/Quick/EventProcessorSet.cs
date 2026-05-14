#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using MMP.Herald.Addons.Compliance;
using MMP.Herald.Pipeline;
using MMP.Herald.Pipeline.Processors;

namespace MMP.Herald.Quick;

/// <summary>
/// Manages the event processor list for a QuickLogBuilder.
/// Supports named registrations with optional protection (survives Reset).
/// </summary>
public sealed class EventProcessorSet
{
    private readonly QuickLogBuilder _builder;
    internal readonly List<NamedRegistration<ILogEventProcessor>> Items = new();

    internal EventProcessorSet(QuickLogBuilder builder) => _builder = builder;

    // -- Add --

    /// <summary>Add a processor (named by its type).</summary>
    public QuickLogBuilder Add(ILogEventProcessor processor) {
        Items.Add(new NamedRegistration<ILogEventProcessor>(processor.GetType().Name, processor));
        return _builder;
    }

    /// <summary>Add a named processor.</summary>
    public QuickLogBuilder Add(string name, ILogEventProcessor processor, bool protect = false) {
        Items.Add(new NamedRegistration<ILogEventProcessor>(name, processor, protect));
        return _builder;
    }

    /// <summary>Add compiled redaction rules.</summary>
    public QuickLogBuilder AddRedaction(params CompiledRedactionRule[] rules) {
        Items.Add(new NamedRegistration<ILogEventProcessor>(
            "CompiledRedaction", new CompiledRedactionProcessor(rules)));
        return _builder;
    }

    /// <summary>
    /// Enterprise only. Parse each rule string with <see cref="RedactionRuleParser"/>
    /// and register the resulting rules as a single redaction processor. Parsing
    /// happens once, at builder time; the compiled rules carry pre-parsed
    /// <c>When</c> predicates that every subsequent event re-uses.
    /// </summary>
    public QuickLogBuilder AddRedactionRules(params string[] ruleStrings) {
        HeraldEditionGate.Require(HeraldEdition.Enterprise, "Redaction Rule DSL");
        ArgumentNullException.ThrowIfNull(ruleStrings);

        var compiled = ruleStrings.Select(RedactionRuleParser.Parse).ToArray();
        return AddRedaction(compiled);
    }

    // -- Remove --

    /// <summary>Remove a processor by name.</summary>
    public QuickLogBuilder Remove(string name) {
        Items.RemoveAll(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        return _builder;
    }

    /// <summary>Remove processors of a specific type (non-protected only).</summary>
    public QuickLogBuilder RemoveOfType<T>() where T : ILogEventProcessor {
        Items.RemoveAll(r => r.Value is T && !r.IsProtected);
        return _builder;
    }

    /// <summary>Remove all non-protected processors.</summary>
    public QuickLogBuilder Clear() {
        Items.RemoveAll(static r => !r.IsProtected);
        return _builder;
    }

    /// <summary>Remove all processors including protected ones.</summary>
    public QuickLogBuilder ClearAll() {
        Items.Clear();
        return _builder;
    }

    // -- Query --

    /// <summary>Get a named processor or null.</summary>
    public T? Get<T>(string name) where T : class, ILogEventProcessor =>
        NamedItemQuery.Get<T, ILogEventProcessor>(Items, name);

    /// <summary>Get a processor by type (first match).</summary>
    public T? Get<T>() where T : class, ILogEventProcessor =>
        NamedItemQuery.GetByType<T, ILogEventProcessor>(Items);

    /// <summary>Check if a processor with the given name exists.</summary>
    public bool Has(string name) =>
        NamedItemQuery.Has(Items, name);

    /// <summary>Current processor count.</summary>
    public int Count => Items.Count;

    // -- Replace --

    /// <summary>Replace a named processor, preserving protection status.</summary>
    public bool Replace(string name, ILogEventProcessor newProcessor) =>
        NamedItemQuery.Replace(Items, name, newProcessor);
}
