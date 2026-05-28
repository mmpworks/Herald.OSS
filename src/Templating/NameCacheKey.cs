// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace MMP.Herald.Templating;

/// <summary>
/// Composite cache key for <see cref="NameResolverCache"/>. Carries the
/// policy identity, the raw template string, and an optional discriminator
/// reserved for future per-tenant / per-template overrides.
///
/// <para>
/// <b>Identity-keyed, not content-keyed.</b> Both <see cref="Policy"/> and
/// <see cref="Template"/> participate in equality and hashing by <em>reference
/// identity</em> (<see cref="object.ReferenceEquals"/> +
/// <see cref="RuntimeHelpers.GetHashCode(object)"/>), not by value. Typed-args
/// templates are compile-time string literals, which the runtime interns to a
/// single stable reference shared across every dispatch site — so the
/// reference key is O(1) per lookup. The previous synthesised record-struct
/// equality hashed the template by content (<c>string.GetHashCode</c>, O(length),
/// recomputed on every call); that per-call content hash grew with template
/// length / arity and was the super-linear term behind the high-arity
/// typed-args cliff. Reference-keying removes that term. Non-interned templates
/// (runtime-built / interpolated / concatenated) get a fresh reference per
/// call and therefore never match a cached slot — <see cref="NameResolverCache"/>
/// guards the insert path so those resolve-each-call without growing the cache.
/// </para>
///
/// <para>
/// <b>Policy reference, not <see cref="IPropertyNamingPolicy.Id"/>.</b> Two
/// distinct policy instances that report identical ids remain distinct cache
/// slots. Reference identity is the cheap, correct check Herald's policies are
/// designed for — every built-in policy is a singleton instance.
/// </para>
///
/// <para>
/// Hand-written <c>readonly struct</c> rather than a <c>record struct</c> so
/// equality/hashing are reference-keyed; the synthesised record members would
/// reintroduce the content hash. C# 13 / .NET 8 portable.
/// </para>
/// </summary>
internal readonly struct NameCacheKey : IEquatable<NameCacheKey>
{
    /// <summary>
    /// The active <see cref="IPropertyNamingPolicy"/>. Reference identity is the
    /// uniqueness carrier; <c>Id</c> string is informational only.
    /// </summary>
    public readonly IPropertyNamingPolicy Policy;

    /// <summary>Raw message template string. Keyed by reference identity.</summary>
    public readonly string Template;

    /// <summary>
    /// Reserved for future per-tenant / per-template overrides. V1 always passes
    /// null. The dimension is present today so a tenant-aware override surface
    /// in V2 can land without a cache-key shape change.
    /// </summary>
    public readonly object? Discriminator;

    public NameCacheKey(IPropertyNamingPolicy policy, string template, object? discriminator = null)
    {
        Policy = policy;
        Template = template;
        Discriminator = discriminator;
    }

    // Policy and Template are reference-keyed (interned literals / singleton
    // policies → stable references). Discriminator is null on the V1 path; when
    // a future override surface populates it, value equality is the right
    // contract for that axis.
    public bool Equals(NameCacheKey other) =>
        ReferenceEquals(Policy, other.Policy)
        && ReferenceEquals(Template, other.Template)
        && Equals(Discriminator, other.Discriminator);

    public override bool Equals(object? obj) => obj is NameCacheKey k && Equals(k);

    public override int GetHashCode() => HashCode.Combine(
        RuntimeHelpers.GetHashCode(Policy),
        RuntimeHelpers.GetHashCode(Template),
        Discriminator);
}
