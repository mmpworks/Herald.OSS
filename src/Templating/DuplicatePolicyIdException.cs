// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;

namespace MMP.Herald.Templating;

/// <summary>
/// Thrown when <c>NamingPolicyRegistry.Register</c> sees a second policy with
/// the same <see cref="IPropertyNamingPolicy.Id"/> as one already registered,
/// and the two policies have different <see cref="Type"/>s.
///
/// <para>
/// Idempotent re-registration of the <i>same</i> policy type is allowed — this
/// protects against module-initializer races where two threads independently
/// register the built-ins on first touch. Genuinely-different policies sharing
/// an Id would corrupt JSON round-trip and is rejected loudly at the second
/// registration.
/// </para>
/// </summary>
public sealed class DuplicatePolicyIdException : Exception
{
    /// <summary>The id that two distinct policy types tried to claim.</summary>
    public string PolicyId { get; }

    /// <summary>The first policy type that registered against the id.</summary>
    public Type ExistingType { get; }

    /// <summary>The second policy type whose registration was rejected.</summary>
    public Type AttemptedType { get; }

    /// <summary>
    /// Construct with the conflicting id and the two policy types involved.
    /// </summary>
    public DuplicatePolicyIdException(string policyId, Type existingType, Type attemptedType)
        : base($"Naming policy id '{policyId}' is already registered to " +
               $"'{existingType.FullName}'. Cannot register the same id for a " +
               $"different type '{attemptedType.FullName}'. Pick a different " +
               $"Id property on the second policy.")
    {
        PolicyId = policyId;
        ExistingType = existingType;
        AttemptedType = attemptedType;
    }
}
