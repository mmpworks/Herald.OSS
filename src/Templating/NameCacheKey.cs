// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;

namespace MMP.Herald.Templating;

/// <summary>
/// Composite cache key for <see cref="NameResolverCache"/>. Carries the
/// policy identity, the raw template string, and an optional discriminator
/// reserved for future per-tenant / per-template overrides.
///
/// <para>
/// <b>Why a record struct, not a value-tuple.</b> The previous
/// <c>(Type, string)</c> tuple was concise but extending it to three
/// components is harder to read at call sites, and the discriminator dimension
/// is a stable axis we want named at the type level. Equality and hashing are
/// the synthesised record-struct defaults, which match the tuple's behavior
/// one-for-one (the discriminator participates via its own equality contract,
/// or is no-op when null on both sides).
/// </para>
///
/// <para>
/// <b>Policy reference, not <see cref="IPropertyNamingPolicy.Id"/>.</b> Two
/// distinct policy instances that report identical ids remain distinct cache
/// slots. The ConcurrentDictionary uses reference equality through
/// <see cref="IPropertyNamingPolicy"/>'s default <c>object.Equals</c> override
/// — that's the cheap, correct identity check Herald's policies are designed
/// for (every built-in policy is a singleton instance).
/// </para>
/// </summary>
/// <param name="Policy">
/// The active <see cref="IPropertyNamingPolicy"/>. Reference identity is the
/// uniqueness carrier; <c>Id</c> string is informational only.
/// </param>
/// <param name="Template">Raw message template string.</param>
/// <param name="Discriminator">
/// Reserved for future per-tenant / per-template overrides. V1 always passes
/// null. The dimension is present today so a tenant-aware override surface
/// in V2 can land without a cache-key shape change.
/// </param>
internal readonly record struct NameCacheKey(
    IPropertyNamingPolicy Policy,
    string Template,
    object? Discriminator = null);
