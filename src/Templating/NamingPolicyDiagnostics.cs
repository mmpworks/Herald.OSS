// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

namespace MMP.Herald.Templating;

/// <summary>
/// Snapshot of property-naming-policy diagnostics for a single
/// <c>StructuredLogger</c>. Surfaced via <c>StructuredLogger.GetNamingPolicyDiagnostics()</c>
/// so operators rolling out a convention change can see what the policy is
/// doing in production.
///
/// <para>
/// <b>Source-gen vs runtime accounting.</b> The <c>[HeraldLog]</c> source generator
/// pre-resolves names at compile time and emits string literals — those calls
/// never touch the runtime cache. They contribute to <see cref="CompileTimeResolutions"/>
/// only; <see cref="CacheHits"/>, <see cref="CacheMisses"/>, and
/// <see cref="FallbackCount"/> reflect the hand-written typed-args path only.
/// In a heavily source-gen'd app it is normal to see <see cref="ResolutionCount"/>
/// at zero while <see cref="CompileTimeResolutions"/> grows.
/// </para>
/// </summary>
/// <param name="PolicyId">The active policy's <c>Id</c> at snapshot time (e.g. <c>"pascal"</c>).</param>
/// <param name="ResolutionCount">
/// Total runtime-path resolutions handled (sum of cache hits + cache misses).
/// Does not include source-gen baked names.
/// </param>
/// <param name="CacheHits">Resolutions served from the process-static frozen cache.</param>
/// <param name="CacheMisses">Resolutions that had to call <c>IPropertyNamingPolicy.ResolveAll</c> and populate the cache.</param>
/// <param name="FallbackCount">Slots where the token name was missing and the caller-argument-expression fallback was used.</param>
/// <param name="CompileTimeResolutions">Calls dispatched through source-gen-baked name literals (the <c>[HeraldLog]</c> path).</param>
public sealed record NamingPolicyDiagnostics(
    string PolicyId,
    long ResolutionCount,
    long CacheHits,
    long CacheMisses,
    long FallbackCount,
    long CompileTimeResolutions);
