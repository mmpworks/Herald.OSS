#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Destructuring;

/// <summary>
/// Applies the set of registered Serilog <see cref="IDestructuringPolicy"/>
/// objects to a raw value, returning a <see cref="LogEventPropertyValue"/> tree
/// if any policy matches, or <c>null</c> if no policy claims the value.
///
/// <para>
/// This is the capture-time application point for Serilog-shaped policies.
/// When a policy matches, the returned tree replaces the default reflection
/// walk in the mirror <see cref="LogEvent.Properties"/> dictionary.  Secrets
/// stripped by the policy therefore never appear in <c>Properties</c>.
/// </para>
///
/// <para>
/// <strong>Security contract:</strong>  this applicator is the enforcement
/// point.  A no-op (returning null for all values) means the default
/// projector runs, which exposes every public property.  Always verify that
/// policies are being added and that the applicator is threaded through to the
/// mirror projection site.
/// </para>
///
/// <para>
/// Thread safety: policies are added during single-threaded configuration;
/// <see cref="Apply"/> is called during event dispatch (potentially concurrent)
/// but only reads from <c>_policies</c>, which is frozen before first use.
/// </para>
/// </summary>
internal sealed class SerilogDestructuringApplicator : MMP.Herald.Adapters.ICaptureRedactor
{
    private readonly List<IDestructuringPolicy> _policies = new();
    private readonly ILogEventPropertyValueFactory _factory;

    internal SerilogDestructuringApplicator()
    {
        // Access DefaultValueFactory (internal, same assembly). If null, a future
        // refactor removed the P1 seam and we would have thrown in the bridge ctor
        // first. Defensive null check here for belt-and-braces.
        _factory = LogEventValueProjector.DefaultValueFactory
            ?? throw new InvalidOperationException(
                "P1 DefaultValueFactory is not accessible; Serilog destructuring cannot be applied.");
    }

    /// <summary>Whether any policies have been registered.</summary>
    internal bool HasPolicies => _policies.Count > 0;

    /// <summary>
    /// Add a policy to the chain.  Called once per
    /// <see cref="Configuration.LoggerDestructuringConfiguration.With"/> call.
    /// </summary>
    internal void Add(IDestructuringPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policies.Add(policy);
    }

    /// <summary>
    /// Try to apply a registered policy to <paramref name="value"/>.
    /// Returns the policy-produced tree if a policy matched, or <c>null</c>
    /// if no policy handled the value (fall through to default projection).
    /// </summary>
    /// <param name="value">The raw object to destructure. Null-safe.</param>
    [RequiresUnreferencedCode(
        "Serilog destructuring policies may walk arbitrary object graphs via reflection.")]
    internal LogEventPropertyValue? Apply(object? value)
    {
        if (value is null || _policies.Count == 0) return null;

        for (var i = 0; i < _policies.Count; i++)
        {
            try
            {
                if (_policies[i].TryDestructure(value, _factory, out var tree))
                    return tree;
            }
            catch
            {
                // A throwing policy is not a signal to abort the chain.
                // Skip the broken policy and let the next one try.
            }
        }
        return null;
    }

    /// <summary>
    /// Run the policy chain against <paramref name="value"/> and, when a policy
    /// claims it, return a NATIVE-renderable redacted value (dictionary / list /
    /// scalar) in <paramref name="redacted"/>.  This is the capture-time hook the
    /// native pipeline uses so a registered destructuring/redaction policy strips
    /// secrets BEFORE the event reaches any sink — making redaction sink-independent
    /// rather than only firing on the mirror <c>WriteTo.Sink</c> projection path.
    ///
    /// <para>
    /// <strong>Security contract.</strong>  Returning <c>true</c> means the raw
    /// object (which may carry a secret) must be REPLACED by <paramref name="redacted"/>
    /// in the captured property, so the secret never lands in the event the sinks
    /// render.  Returning <c>false</c> means no policy matched and the caller keeps
    /// the original value (default projection).
    /// </para>
    /// </summary>
    /// <param name="value">The raw object to redact. Null-safe.</param>
    /// <param name="redacted">The native-renderable redacted value when a policy matched.</param>
    [RequiresUnreferencedCode(
        "Serilog destructuring policies may walk arbitrary object graphs via reflection.")]
    internal bool TryRedactNative(object? value, out object? redacted)
    {
        var tree = Apply(value);
        if (tree is null)
        {
            redacted = null;
            return false;
        }

        redacted = ToNative(tree);
        return true;
    }

    // Explicit ICaptureRedactor implementation — lets the engine-side
    // HeraldAdapterCore apply redaction through the engine interface without the
    // engine ever naming this mirror type (Guard1 confinement). Forwards to the
    // existing internal members so all in-assembly callers are unchanged.
    bool MMP.Herald.Adapters.ICaptureRedactor.HasPolicies => HasPolicies;

    [RequiresUnreferencedCode(
        "Serilog destructuring policies may walk arbitrary object graphs via reflection.")]
    bool MMP.Herald.Adapters.ICaptureRedactor.TryRedactNative(object? value, out object? redacted)
        => TryRedactNative(value, out redacted);

    // Convert a policy-produced Serilog value tree to a native-renderable value.
    // ScalarValue -> the boxed scalar; StructureValue -> ordered name/value dictionary;
    // SequenceValue -> list; DictionaryValue -> keyed dictionary. Native sinks already
    // know how to render dictionaries/lists/scalars, so the redacted shape carries
    // through every sink kind identically.
    //
    // Cognitive complexity note: one switch over the four node types, each branch a
    // single linear projection; recursion depth follows the policy's own tree depth.
    private static object? ToNative(LogEventPropertyValue node) => node switch
    {
        ScalarValue sv    => sv.Value,
        StructureValue s  => StructureToNative(s),
        SequenceValue sq  => SequenceToNative(sq),
        DictionaryValue d => DictionaryToNative(d),
        _                 => node.ToString(),
    };

    private static Dictionary<string, object?> StructureToNative(StructureValue s)
    {
        // Ordinal, insertion-ordered: a redacted POCO renders its approved fields
        // in declaration order on a JSON sink, the same shape Serilog emits.
        var map = new Dictionary<string, object?>(s.Properties.Count, StringComparer.Ordinal);
        foreach (var prop in s.Properties)
            map[prop.Name] = ToNative(prop.Value);
        return map;
    }

    private static List<object?> SequenceToNative(SequenceValue sq)
    {
        var list = new List<object?>(sq.Elements.Count);
        foreach (var elem in sq.Elements)
            list.Add(ToNative(elem));
        return list;
    }

    private static Dictionary<object, object?> DictionaryToNative(DictionaryValue d)
    {
        var map = new Dictionary<object, object?>(d.Elements.Count);
        foreach (var entry in d.Elements)
        {
            // Scalar keys; fall back to a stable string for a null key so the
            // entry is not silently dropped.
            var key = entry.Key.Value ?? "null";
            map[key] = ToNative(entry.Value);
        }
        return map;
    }
}
