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
internal sealed class SerilogDestructuringApplicator
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
}
