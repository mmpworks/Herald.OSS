// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;

namespace MMP.Herald.Templating;

/// <summary>
/// Thrown when a JSON config requests a <c>"namingPolicy"</c> identifier that
/// no policy has registered against in <c>NamingPolicyRegistry</c>.
///
/// <para>
/// Two code paths reach this exception with different semantics:
/// <list type="bullet">
///   <item><b>Cold-start <c>Reload</c></b> (the typical case during application
///       startup): the exception is thrown directly. The caller is expected to
///       register custom policies before the first <c>Reload</c>.</item>
///   <item><b>Hot-reload</b> (replacing a running pipeline): the
///       <c>HotReloadBootstrap</c> path catches this exception, keeps the
///       previously-active policy, and surfaces a <c>ReloadDegraded</c>
///       diagnostic event instead of throwing. Operators see the failure
///       without losing the live pipeline.</item>
/// </list>
/// </para>
/// </summary>
public sealed class UnknownNamingPolicyException : Exception
{
    /// <summary>The unrecognized policy identifier as it appeared in the JSON.</summary>
    public string PolicyId { get; }

    /// <summary>
    /// Construct with the unknown id; the <see cref="Exception.Message"/> includes a
    /// remediation hint that points consumers at the registry.
    /// </summary>
    public UnknownNamingPolicyException(string policyId)
        : base($"Unknown naming policy '{policyId}'. " +
               "Register the policy via NamingPolicyRegistry.Register(...) " +
               "before the first QuickLogBuilder.Reload(json) call, " +
               "or update the JSON to one of: 'pascal', 'camel', 'snake'.")
    {
        PolicyId = policyId;
    }
}
